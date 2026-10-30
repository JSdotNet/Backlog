using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync.DevbookSubjects;

/// <summary>
/// The schedule devbook subject replication needs: a timer that runs
/// <see cref="DevbookSubjectSyncSession.SyncAsync"/> on its own, so a state set
/// on one desktop reaches the others without anybody opening Settings.
/// <para>
/// A sibling of <see cref="Annotations.AnnotationSyncWorker"/> rather than a
/// second exchange inside it, for the reasoning <c>NotificationSyncWorker</c>
/// and <see cref="Sessions.SessionSyncWorker"/> record: one loop per feed makes
/// "one exchange failing does not stop the other" true by construction. The
/// mechanism — the two gates, the overlap guard, the resolve-per-cycle, the catch
/// that keeps a tick from ending the process — is that worker's, copied rather
/// than shared, and its comments are there rather than here.
/// </para>
/// <para>
/// A cycle against a service that predates subject state ends as a success that
/// says <see cref="DevbookSubjectSyncSummary.NotYetServed"/>, so
/// <see cref="LastError"/> stays empty while the deployed service catches up.
/// </para>
/// </summary>
public sealed class DevbookSubjectSyncWorker : IDisposable
{
    /// <summary>How long after the loop starts the first cycle runs — staggered
    /// behind every other loop.</summary>
    internal static readonly TimeSpan FirstCycleDelay = TimeSpan.FromSeconds(16);

    /// <summary>How often a cycle runs after the first one — the other loops'
    /// five minutes.</summary>
    internal static readonly TimeSpan CyclePeriod = TimeSpan.FromMinutes(5);

    public const string UnexpectedFailure = TaskSyncWorker.UnexpectedFailure;

    private readonly Lock _gate = new();

    private readonly IServiceProvider _services;
    private readonly IAppFeatureSettings _features;
    private readonly IDeviceCredentialStore _credentials;
    private readonly IDevbookSubjectSyncStateStore _state;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    private readonly CancellationTokenSource _lifetime = new();

    private int _cycleInFlight;

    private ITimer? _timer;
    private bool _disposed;

    public DevbookSubjectSyncWorker(
        IServiceProvider services,
        IAppFeatureSettings features,
        IDeviceCredentialStore credentials,
        IDevbookSubjectSyncStateStore state,
        TimeProvider time,
        ILogger<DevbookSubjectSyncWorker>? log = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(time);

        _services = services;
        _features = features;
        _credentials = credentials;
        _state = state;
        _time = time;
        _log = log ?? NullLogger<DevbookSubjectSyncWorker>.Instance;

        _features.Changed += OnGateChanged;
        _credentials.Changed += OnGateChanged;

        ApplyGates();
    }

    /// <summary>Raised at both ends of a cycle, on a thread pool thread.</summary>
    public event Action? Changed;

    /// <summary>What the last exchange that succeeded did, or null when none has.</summary>
    public DevbookSubjectSyncSummary? LastSummary { get; private set; }

    /// <summary>Why the last cycle failed, or null when the last one worked.</summary>
    public string? LastError { get; private set; }

    /// <summary>True while a cycle is in flight.</summary>
    public bool IsSyncing => Volatile.Read(ref _cycleInFlight) == 1;

    /// <summary>Where this device's subject progress is kept.</summary>
    public string StorePath => _state.StorePath;

    /// <summary>Runs a cycle now, and returns before it has done anything.</summary>
    public void RequestSync() => _ = RequestSyncAsync();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }

        _features.Changed -= OnGateChanged;
        _credentials.Changed -= OnGateChanged;

        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    /// <summary>What <see cref="RequestSync"/> discards, so a test can wait for
    /// the dispatch.</summary>
    internal Task RequestSyncAsync()
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
        }

        return Task.Run(RunGuardedAsync, CancellationToken.None);
    }

    /// <summary>Both gates: the one <c>sync</c> feature every loop answers to,
    /// and a paired device to replicate under.</summary>
    private bool ShouldRun => _features.IsEnabled(SyncFeatures.Sync) && _credentials.Current is not null;

    private void OnGateChanged() => ApplyGates();

    private void ApplyGates()
    {
        lock (_gate)
        {
            if (_disposed) return;

            if (!ShouldRun)
            {
                _timer?.Dispose();
                _timer = null;
                return;
            }

            _timer ??= _time.CreateTimer(_ => OnTickElapsed(), state: null, FirstCycleDelay, CyclePeriod);
        }
    }

    private async void OnTickElapsed()
    {
        try
        {
            await RunGuardedAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A devbook subject sync tick failed outside the cycle it was starting.");
        }
    }

    private async Task RunGuardedAsync()
    {
        try
        {
            await RunCycleAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A devbook subject sync cycle failed unexpectedly.");
        }
    }

    private async Task RunCycleAsync()
    {
        if (!ShouldRun) return;

        if (Interlocked.CompareExchange(ref _cycleInFlight, 1, 0) != 0) return;

        Changed?.Invoke();

        try
        {
            var session = ResolveSession();

            if (session is null) return;

            var result = await RefusedTokenRerun.RunAsync(
                _services, () => session.SyncAsync(_lifetime.Token), _log, "devbook subject").ConfigureAwait(false);

            if (result.IsSuccess)
            {
                LastSummary = result.Value;
                LastError = null;
            }
            else
            {
                LastError = result.Error.Message;
                _log.LogInformation(
                    "A devbook subject sync cycle did not complete: {Code} {Message}", result.Error.Code, result.Error.Message);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window closed mid-exchange.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A devbook subject sync cycle threw.");
            LastError = UnexpectedFailure;
        }
        finally
        {
            Volatile.Write(ref _cycleInFlight, 0);
            Changed?.Invoke();
        }
    }

    /// <summary>A session for this cycle, where this host composed one; a head
    /// that registered the loop without its stores has nothing to do rather than
    /// being broken.</summary>
    private DevbookSubjectSyncSession? ResolveSession()
    {
        try
        {
            return _services.GetService<DevbookSubjectSyncSession>();
        }
        catch (InvalidOperationException ex)
        {
            _log.LogInformation(ex, "Devbook subject sync is not composed in this host, so the loop has nothing to do.");
            return null;
        }
    }
}
