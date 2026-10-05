using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Modules.Tasks.Features.SyncLinkedTasks;

/// <summary>
/// The desktop's timer for linked tasks: runs <see cref="SyncLinkedTasksCommand"/>
/// for every enabled connected target once its interval has passed, and for all of
/// them at once when the Tasks pane asks.
/// <para>
/// A plain object with a timer in it, constructed by the head after <c>Build()</c>,
/// for the reason <c>TaskSyncWorker</c> gives at length: the MAUI head has no
/// generic host to start an <c>IHostedService</c>, so a hosted service would run in
/// the browser harness and never in the app this exists for.
/// </para>
/// <para>
/// The timer ticks every <see cref="TickPeriod"/>, which is not the sync interval:
/// each target carries its own (ADR 0020, §9), and a tick only asks which of them
/// are due. A target whose sync failed is not due again until its interval has
/// passed since the attempt, so a source that is down is asked once an interval
/// rather than once a minute.
/// </para>
/// <para>
/// The handler is resolved per target from a fresh scope, because it and the
/// repository behind it are scoped; this singleton holds neither. With no
/// connector registered there is nothing to sync, and the worker starts no timer
/// at all.
/// </para>
/// </summary>
public sealed class LinkedTaskSyncWorker : ILinkedTaskSyncTrigger, IDisposable
{
    /// <summary>How long after the app starts the first tick runs. Not zero: the
    /// first seconds of a launch belong to drawing the window, not to the
    /// network.</summary>
    internal static readonly TimeSpan FirstTickDelay = TimeSpan.FromSeconds(30);

    /// <summary>How often the worker asks which targets are due.</summary>
    internal static readonly TimeSpan TickPeriod = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly IConnectedTargets _targets;
    private readonly HashSet<string> _connectorIds;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    /// <summary>One run at a time. A tick that finds a run in flight skips; an
    /// on-demand request waits its turn and then runs, so pressing the button
    /// always syncs. Two runs at once would fetch the same target twice and race
    /// each other's saves of its progress.
    /// <para>
    /// Never disposed, on purpose: a run that is still finishing when the window
    /// closes releases it, and a disposed semaphore would turn that release into an
    /// exception on a thread nobody is watching. It allocates no wait handle, so
    /// there is nothing to free.
    /// </para></summary>
    private readonly SemaphoreSlim _run = new(1, 1);

    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _stopping;

    /// <summary>When each target was last attempted in this process, success or
    /// not. Only read and written while <see cref="_run"/> is held.</summary>
    private readonly Dictionary<(string ConnectorId, string Target), DateTimeOffset> _lastAttempt = [];

    private readonly Lock _gate = new();
    private ITimer? _timer;
    private bool _disposed;

    public LinkedTaskSyncWorker(
        IServiceScopeFactory scopes,
        IConnectedTargets targets,
        IEnumerable<ITaskConnector> connectors,
        TimeProvider time,
        ILogger<LinkedTaskSyncWorker>? log = null)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(connectors);
        ArgumentNullException.ThrowIfNull(time);

        _scopes = scopes;
        _targets = targets;
        _connectorIds = [.. connectors.Select(connector => connector.Descriptor.Id)];
        _time = time;
        _log = log ?? NullLogger<LinkedTaskSyncWorker>.Instance;
        _stopping = _lifetime.Token;

        if (_connectorIds.Count == 0) return;

        // TimeProvider.CreateTimer so a test drives the schedule rather than
        // waiting for it.
        _timer = _time.CreateTimer(_ => OnTick(), state: null, FirstTickDelay, TickPeriod);
    }

    /// <summary>
    /// Syncs every enabled target whose connector is registered, now, whether or not
    /// its interval has passed — the Tasks pane's on-demand sync. Waits for a run
    /// already in flight, then runs. Never throws: a target that fails is logged and
    /// the rest still run.
    /// </summary>
    public Task RequestSync() => RunAsync(onlyDue: false, waitForTurn: true);

    /// <summary>One tick: the due targets only, and nothing at all when a run is
    /// already in flight. What the timer calls; internal so a test can await
    /// it.</summary>
    internal Task TickAsync() => RunAsync(onlyDue: true, waitForTurn: false);

    /// <summary>Whether a target is due: never synced, or its interval has passed
    /// since the later of its last success and this process's last attempt.</summary>
    internal static bool IsDue(ConnectedTarget target, DateTimeOffset? lastAttempt, DateTimeOffset now)
    {
        var last = target.LastSyncedAt is { } synced && lastAttempt is { } attempted
            ? (synced > attempted ? synced : attempted)
            : target.LastSyncedAt ?? lastAttempt;

        return last is null || now - last.Value >= target.SyncInterval;
    }

    /// <summary>Stops the timer and asks a run in flight to stop.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }

        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    /// <summary>A tick, which must not throw: it runs on a thread pool thread with
    /// nobody to hand a failure to, and an escaping exception there ends the
    /// process.</summary>
    private async void OnTick()
    {
        try
        {
            await TickAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A linked task sync tick failed outside the run it was starting.");
        }
    }

    private async Task RunAsync(bool onlyDue, bool waitForTurn)
    {
        lock (_gate)
        {
            if (_disposed) return;
        }

        if (_connectorIds.Count == 0) return;

        try
        {
            if (waitForTurn)
            {
                await _run.WaitAsync(_stopping).ConfigureAwait(false);
            }
            else if (!await _run.WaitAsync(0, _stopping).ConfigureAwait(false))
            {
                return;
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return;
        }

        try
        {
            foreach (var target in _targets.List())
            {
                if (_stopping.IsCancellationRequested) return;
                if (!target.Enabled || !_connectorIds.Contains(target.ConnectorId)) continue;

                var now = _time.GetUtcNow();
                var key = (target.ConnectorId, target.Target.ToUpperInvariant());
                if (onlyDue && !IsDue(target, _lastAttempt.TryGetValue(key, out var attempted) ? attempted : null, now)) continue;

                _lastAttempt[key] = now;
                await SyncAsync(target).ConfigureAwait(false);
            }
        }
        finally
        {
            _run.Release();
        }
    }

    /// <summary>One target, with its own catch: one source being down is no reason
    /// for the next target not to sync.</summary>
    private async Task SyncAsync(ConnectedTarget target)
    {
        try
        {
            var scope = _scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var handler = scope.ServiceProvider
                    .GetRequiredService<ICommandHandler<SyncLinkedTasksCommand, Result<LinkedTaskSyncSummary>>>();

                var result = await handler
                    .Handle(new SyncLinkedTasksCommand(target.ConnectorId, target.Target), _stopping)
                    .ConfigureAwait(false);

                if (result.IsFailure)
                {
                    _log.LogWarning(
                        "Linked task sync for {Connector} {Target} did not complete: {Code} {Message}",
                        target.ConnectorId, target.Target, result.Error.Code, result.Error.Message);
                }
                else
                {
                    _log.LogDebug(
                        "Linked task sync for {Connector} {Target}: {Summary}",
                        target.ConnectorId, target.Target, result.Value);
                }
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // The window closed mid-sync. Nothing failed.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Linked task sync for {Connector} {Target} threw.", target.ConnectorId, target.Target);
        }
    }
}
