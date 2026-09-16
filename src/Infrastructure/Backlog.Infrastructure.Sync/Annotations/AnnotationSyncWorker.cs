using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync.Annotations;

/// <summary>
/// The schedule annotation replication needs: a timer that runs
/// <see cref="AnnotationSyncSession.SyncAsync"/> on its own, so a remark left on
/// one desktop reaches the others without anybody opening Settings.
/// <para>
/// A third sibling of <see cref="TaskSyncWorker"/> and
/// <see cref="Sessions.SessionSyncWorker"/> rather than a third exchange inside
/// either, for the whole of the reasoning the session worker records: one loop
/// per feed makes "one exchange failing does not stop the other" true by
/// construction, at the cost of one timer callback every five minutes. The
/// mechanism — the two gates, the overlap guard, the resolve-per-cycle, the
/// catch that keeps a tick from ending the process — is that worker's, copied
/// rather than shared, and the comments on it are there rather than here.
/// </para>
/// </summary>
public sealed class AnnotationSyncWorker : IDisposable
{
    /// <summary>How long after the loop starts the first cycle runs. Staggered
    /// behind both other loops so three exchanges do not fire into the seconds
    /// that belong to drawing the window.</summary>
    internal static readonly TimeSpan FirstCycleDelay = TimeSpan.FromSeconds(11);

    /// <summary>How often a cycle runs after the first one — the other two
    /// loops' five minutes, for the same battery-and-radio budget.</summary>
    internal static readonly TimeSpan CyclePeriod = TimeSpan.FromMinutes(5);

    public const string UnexpectedFailure = TaskSyncWorker.UnexpectedFailure;

    private readonly Lock _gate = new();

    private readonly IServiceProvider _services;
    private readonly IAppFeatureSettings _features;
    private readonly IDeviceCredentialStore _credentials;
    private readonly IAnnotationSyncStateStore _state;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    private readonly CancellationTokenSource _lifetime = new();

    private int _cycleInFlight;

    private ITimer? _timer;
    private bool _disposed;

    public AnnotationSyncWorker(
        IServiceProvider services,
        IAppFeatureSettings features,
        IDeviceCredentialStore credentials,
        IAnnotationSyncStateStore state,
        TimeProvider time,
        ILogger<AnnotationSyncWorker>? log = null)
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
        _log = log ?? NullLogger<AnnotationSyncWorker>.Instance;

        _features.Changed += OnGateChanged;
        _credentials.Changed += OnGateChanged;

        ApplyGates();
    }

    /// <summary>Raised at both ends of a cycle, on a thread pool thread.</summary>
    public event Action? Changed;

    /// <summary>What the last exchange that succeeded did, or null when none has.</summary>
    public AnnotationSyncSummary? LastSummary { get; private set; }

    /// <summary>Why the last cycle failed, or null when the last one worked.</summary>
    public string? LastError { get; private set; }

    /// <summary>True while a cycle is in flight.</summary>
    public bool IsSyncing => Volatile.Read(ref _cycleInFlight) == 1;

    /// <summary>Where this device's annotation progress is kept.</summary>
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
    /// the dispatch the button does not.</summary>
    internal Task RequestSyncAsync()
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
        }

        return Task.Run(RunGuardedAsync, CancellationToken.None);
    }

    /// <summary>Both gates, read together: the one <c>sync</c> feature every
    /// loop answers to, and a paired device to replicate under.</summary>
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
            _log.LogError(ex, "An annotation sync tick failed outside the cycle it was starting.");
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
            _log.LogError(ex, "An annotation sync cycle failed unexpectedly.");
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

            var result = await session.SyncAsync(_lifetime.Token).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                LastSummary = result.Value;
                LastError = null;
            }
            else
            {
                LastError = result.Error.Message;
                _log.LogInformation(
                    "An annotation sync cycle did not complete: {Code} {Message}", result.Error.Code, result.Error.Message);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window closed mid-exchange.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "An annotation sync cycle threw.");
            LastError = UnexpectedFailure;
        }
        finally
        {
            Volatile.Write(ref _cycleInFlight, 0);
            Changed?.Invoke();
        }
    }

    /// <summary>A session for this cycle, where this host composed one; a head
    /// that registered the loop without a store or an annotation store is
    /// treated as having nothing to do rather than as broken.</summary>
    private AnnotationSyncSession? ResolveSession()
    {
        try
        {
            return _services.GetService<AnnotationSyncSession>();
        }
        catch (InvalidOperationException ex)
        {
            _log.LogInformation(ex, "Annotation sync is not composed in this host, so the loop has nothing to do.");
            return null;
        }
    }
}
