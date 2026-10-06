using System.Collections.Concurrent;

using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Modules.Tasks.Features.CompleteLinkedTask;

/// <summary>
/// The <see cref="ILinkedTaskWriteBack"/> a desktop head registers: each request
/// runs <see cref="CompleteLinkedTaskCommand"/> on the thread pool, so the save that
/// asked returns before the source is called.
/// <para>
/// A plain singleton rather than a hosted service, for the reason
/// <see cref="SyncLinkedTasks.LinkedTaskSyncWorker"/> gives: the MAUI head has no
/// generic host to start one. The handler is resolved from a fresh scope per
/// request, because it and the repository behind it are scoped and the save's own
/// scope may be gone by the time the source answers.
/// </para>
/// <para>
/// <b>The grace.</b> A request waits <see cref="Grace"/> before the handler runs.
/// A tick made by mistake and taken back within it sends nothing, because the
/// handler asks the source only for a task that is still Done when it runs.
/// </para>
/// <para>
/// <b>The pace.</b> At most <see cref="MaxConcurrent"/> write-backs run at once, so
/// ticking off a whole list does not fire a request per task at the source
/// together. Every success asks for the follow-up sync that brings the source's
/// Done in, but the sync is asked once, <see cref="SyncQuietPeriod"/> after the last
/// success of a burst: the sync fetches every target, and one per completion would
/// spend a source's rate limit on the same answer.
/// </para>
/// <para>
/// Nothing is retried and nothing is queued past the process: a write-back the app
/// closed on is simply not made, and the task stays Done here, flagged done-locally
/// by the next sync, as it would have been with the setting off.
/// </para>
/// </summary>
public sealed class LinkedTaskWriteBack : ILinkedTaskWriteBack, IDisposable
{
    /// <summary>How long a request waits before the source is asked.</summary>
    internal static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

    /// <summary>How long after the last success the follow-up sync is asked for.</summary>
    internal static readonly TimeSpan SyncQuietPeriod = TimeSpan.FromSeconds(5);

    /// <summary>How many write-backs may be talking to sources at once.</summary>
    internal const int MaxConcurrent = 2;

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILinkedTaskSync? _sync;
    private readonly ILogger _log;

    /// <summary>The requests still running, so a test can wait for them rather than
    /// sleep. Keyed by a counter, because one task may be asked for twice.</summary>
    private readonly ConcurrentDictionary<long, Task> _running = new();
    private long _next;

    /// <summary>The turns at the sources. Never disposed, for the reason the sync
    /// worker's run lock is not: a run finishing after shutdown releases it.</summary>
    private readonly SemaphoreSlim _turns = new(MaxConcurrent, MaxConcurrent);

    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _stopping;

    private readonly Lock _gate = new();
    private ITimer? _syncTimer;
    private bool _disposed;

    public LinkedTaskWriteBack(
        IServiceScopeFactory scopes,
        TimeProvider time,
        ILinkedTaskSync? sync = null,
        ILogger<LinkedTaskWriteBack>? log = null)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(time);

        _scopes = scopes;
        _time = time;
        _sync = sync;
        _log = log ?? NullLogger<LinkedTaskWriteBack>.Instance;
        _stopping = _lifetime.Token;
    }

    /// <summary>Starts the write-back on the thread pool and returns. Never
    /// throws.</summary>
    public void Request(Guid taskId)
    {
        lock (_gate)
        {
            if (_disposed) return;
        }

        var key = Interlocked.Increment(ref _next);

        // Started without the caller's execution context. The Tasks pane marks its own
        // saves in an async-local and ignores the writes it hears inside that mark; the
        // answer this run writes back is news to it, and would otherwise arrive marked
        // as the pane's own and reach the list only on a reload. Suppressing the flow
        // also leaves the caller's Activity and logging scope behind, which is right
        // for work that outlives the save. Suppressed only when it is not already:
        // suppressing twice throws.
        Task run;
        var suppression = ExecutionContext.IsFlowSuppressed() ? default(AsyncFlowControl?) : ExecutionContext.SuppressFlow();
        try
        {
            // The grace starts now, on the caller's clock, rather than whenever the
            // thread pool gets to the run.
            var grace = Task.Delay(Grace, _time, _stopping);
            run = Task.Run(() => RunAsync(taskId, grace));
        }
        finally
        {
            suppression?.Undo();
        }

        _running[key] = run;

        // Removed once done, so the dictionary holds only what is in flight. Attached
        // after the add, so a run that has already finished is removed at once rather
        // than left behind.
        _ = run.ContinueWith(
            _ => _running.TryRemove(key, out Task? _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Completes once every request made so far has finished. For tests;
    /// the app never waits on a write-back.</summary>
    internal Task WhenIdleAsync() => Task.WhenAll(_running.Values);

    /// <summary>Stops the sync timer and asks every write-back in flight, or still in
    /// its grace, to stop; a stopped one records nothing.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _syncTimer?.Dispose();
            _syncTimer = null;
        }

        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    /// <summary>One request, with its own catch: it runs on a thread pool thread
    /// with nobody to hand a failure to.</summary>
    private async Task RunAsync(Guid taskId, Task grace)
    {
        try
        {
            await grace.ConfigureAwait(false);
            await _turns.WaitAsync(_stopping).ConfigureAwait(false);
            try
            {
                var outcome = await CompleteAsync(taskId).ConfigureAwait(false);
                if (outcome == LinkedTaskWriteBackOutcome.Completed) ScheduleSync();
            }
            finally
            {
                _turns.Release();
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // The app is closing. Nothing was recorded, and nothing needs to be.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Completing linked task {TaskId} at its source threw.", taskId);
        }
    }

    /// <summary>The handler, in a scope of its own that ends with it — the follow-up
    /// sync is asked for outside it.</summary>
    private async Task<LinkedTaskWriteBackOutcome> CompleteAsync(Guid taskId)
    {
        var scope = _scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<CompleteLinkedTaskCommand, Result<LinkedTaskWriteBackOutcome>>>();

            var result = await handler.Handle(new CompleteLinkedTaskCommand(taskId), _stopping).ConfigureAwait(false);
            if (result.IsFailure)
            {
                _log.LogWarning(
                    "Completing linked task {TaskId} at its source did not run: {Code} {Message}",
                    taskId, result.Error.Code, result.Error.Message);
                return LinkedTaskWriteBackOutcome.NotAsked;
            }

            return result.Value;
        }
    }

    /// <summary>Asks for the follow-up sync <see cref="SyncQuietPeriod"/> from now,
    /// moving a sync already asked for rather than adding a second.</summary>
    private void ScheduleSync()
    {
        if (_sync is null) return;

        lock (_gate)
        {
            if (_disposed) return;

            if (_syncTimer is null)
            {
                _syncTimer = _time.CreateTimer(_ => OnSyncDue(), state: null, SyncQuietPeriod, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _syncTimer.Change(SyncQuietPeriod, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>The quiet period passed: one sync for the burst. Must not throw — it
    /// runs on a timer thread — and the sync it asks for never does.</summary>
    private void OnSyncDue()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _syncTimer?.Dispose();
            _syncTimer = null;
        }

        _ = _sync!.RequestSync();
    }
}
