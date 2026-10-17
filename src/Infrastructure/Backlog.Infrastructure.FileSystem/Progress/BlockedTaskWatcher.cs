using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.FileSystem.Progress;

/// <summary>
/// Runs Progress's <see cref="IBlockedTaskPolicy"/> whenever a task was written on this
/// machine, heard through Tasks' <see cref="ITaskChangeSignal"/> — the reaction local ADR
/// 0035 §6 asks for: Progress reacts to a task's blocked mark, and Tasks never calls
/// Progress. A <c>blocked:</c> token typed into an entry and a <c>set_blocked</c> call
/// are both task writes, so both are heard here.
/// <para>
/// <b>Off the writer's thread, one run at a time.</b> The signal is raised on whatever
/// thread wrote; the policy runs on the thread pool in a scope of its own, because the
/// task port it reads is scoped. Signals that arrive while a run is under way fold into
/// one more run after it, so a burst of writes costs two reads, not one per write.
/// </para>
/// <para>
/// A copy the sync pulls is written inside the signal's suppression and so is not heard;
/// the device that marked the task raised the impediment, and it arrives as its own
/// document. A run that fails is logged and never reaches the writer.
/// </para>
/// <para>
/// A plain singleton rather than an <c>IHostedService</c>, for the reason
/// <c>TaskSyncWorker</c> gives: the head resolves it once at startup, and a watcher nobody
/// resolves hears nothing.
/// </para>
/// </summary>
public sealed class BlockedTaskWatcher : IDisposable
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ITaskChangeSignal? _signal;
    private readonly ILogger _log;
    private readonly Lock _sync = new();
    private Task _running = Task.CompletedTask;
    private bool _busy;
    private bool _again;

    public BlockedTaskWatcher(IServiceScopeFactory scopes, ITaskChangeSignal? signal = null, ILogger<BlockedTaskWatcher>? log = null)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        _scopes = scopes;
        _signal = signal;
        _log = log ?? NullLogger<BlockedTaskWatcher>.Instance;

        if (_signal is not null) _signal.Changed += OnTaskWritten;
    }

    /// <summary>The run under way, or the last one — for a test to await.</summary>
    public Task Idle
    {
        get
        {
            lock (_sync) return _running;
        }
    }

    public void Dispose()
    {
        if (_signal is not null) _signal.Changed -= OnTaskWritten;
    }

    private void OnTaskWritten()
    {
        lock (_sync)
        {
            if (_busy)
            {
                _again = true;
                return;
            }

            _busy = true;
            _running = Task.Run(RunAsync);
        }
    }

    private async Task RunAsync()
    {
        while (true)
        {
            try
            {
                var scope = _scopes.CreateAsyncScope();
                await using var _ = scope.ConfigureAwait(false);
                if (scope.ServiceProvider.GetService<IBlockedTaskPolicy>() is { } policy)
                {
                    var raised = await policy.ReactAsync().ConfigureAwait(false);
                    foreach (var impediment in raised)
                    {
                        _log.LogInformation(
                            "Raised impediment {ImpedimentId} for blocked task {TaskId}.",
                            impediment.Id,
                            impediment.HoldsBack.FirstOrDefault());
                    }
                }
            }
#pragma warning disable CA1031 // A failed run must never reach the thread that wrote the task.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _log.LogWarning(ex, "The blocked-task policy could not run; the next task write tries again.");
            }

            lock (_sync)
            {
                if (!_again)
                {
                    _busy = false;
                    return;
                }

                _again = false;
            }
        }
    }
}
