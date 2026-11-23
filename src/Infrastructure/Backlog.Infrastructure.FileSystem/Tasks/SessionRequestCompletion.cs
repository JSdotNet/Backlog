using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.FileSystem.Tasks;

/// <summary>
/// Takes a session request out of the queue once the person finished its work in the
/// app (local ADR 0038 §3): a plan request once every entry of its plan is Done or
/// Archived, an entry request once its entry is. The request is Cancelled, as a press of
/// Cancel would leave it; no session is stopped.
/// <para>
/// Only a Requested or Started request is closed. A Claimed one has a dispatcher starting
/// its session right then, so it is left alone, as the queue's own Cancel leaves it. A plan
/// with no entries is not finished.
/// </para>
/// <para>
/// A cross-context join, here for the reason <see cref="SessionRequestActions"/> is. It
/// sweeps once when composed — so requests left open before it existed close — and again
/// after every local task write <see cref="ITaskChangeSignal"/> reports, the MCP server's
/// included. Sweeps never overlap; writes heard during one run one more sweep after it.
/// </para>
/// </summary>
public sealed class SessionRequestCompletion : IDisposable
{
    private readonly ISessionRequestStore? _queue;
    private readonly Func<CancellationToken, Task<IReadOnlyList<TaskItemDto>>> _entries;
    private readonly ITaskChangeSignal? _signal;
    private readonly IAppFeatureSettings? _features;
    private readonly ILogger _logger;
    private readonly Lock _chain = new();
    private int _dirty;
    private Task _running = Task.CompletedTask;

    /// <param name="queue">The request queue, or null when the host composed none.</param>
    /// <param name="entries">Reads the backlog whole, on a sweep that has a request to judge.
    /// A function rather than <see cref="ITaskItems"/>, which is scoped: the host opens a
    /// scope per read.</param>
    /// <param name="signal">The local task writes; null for a host or test that sweeps by hand.</param>
    public SessionRequestCompletion(
        ISessionRequestStore? queue,
        Func<CancellationToken, Task<IReadOnlyList<TaskItemDto>>> entries,
        ITaskChangeSignal? signal = null,
        IAppFeatureSettings? features = null,
        ILogger<SessionRequestCompletion>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _queue = queue;
        _entries = entries;
        _signal = signal;
        _features = features;
        _logger = logger ?? NullLogger<SessionRequestCompletion>.Instance;

        if (_signal is null || _queue is null) return;

        _signal.Changed += OnChanged;
        OnChanged();
    }

    /// <summary>Completes once no sweep is running or owed. For a test.</summary>
    internal Task Idle => Volatile.Read(ref _running);

    /// <summary>Closes every open request whose work is finished, and answers how many.</summary>
    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        if (_queue is null) return 0;
        if (_features is not null && !_features.IsEnabled(SessionFeatures.SessionRequests)) return 0;

        List<SessionRequest> open =
        [
            .. await _queue.ListAsync(SessionRequestState.Requested, cancellationToken).ConfigureAwait(false),
            .. await _queue.ListAsync(SessionRequestState.Started, cancellationToken).ConfigureAwait(false)
        ];

        if (open.Count == 0) return 0;

        var entries = await _entries(cancellationToken).ConfigureAwait(false);
        var closed = 0;

        foreach (var request in open)
        {
            if (!IsFinished(request, entries)) continue;

            var outcome = await _queue.CancelAsync(request.Id, cancellationToken).ConfigureAwait(false);

            if (outcome.Succeeded) closed++;
        }

        return closed;
    }

    /// <summary>Whether the person finished <paramref name="request"/>'s work: its entry,
    /// or every entry of its plan — the entries an import filed under the plan, and those
    /// carrying its tag with no import of their own, as <c>get_plan_items</c> reads them.</summary>
    public static bool IsFinished(SessionRequest request, IReadOnlyList<TaskItemDto> entries)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(entries);

        if (request.Kind == SessionRequestKind.Entry)
        {
            return entries.FirstOrDefault(entry => entry.Id == request.EntryId) is { } entry && IsClosed(entry);
        }

        if (string.IsNullOrWhiteSpace(request.PlanTag)) return false;

        var tag = "+" + SessionRequests.NormalizeTag(request.PlanTag);
        var plan = entries
            .Where(entry => entry.ImportPlanId is { } owner
                ? string.Equals(owner, tag, StringComparison.Ordinal)
                : entry.Tags.Contains(tag, StringComparer.Ordinal))
            .ToList();

        return plan.Count > 0 && plan.All(IsClosed);
    }

    public void Dispose()
    {
        if (_signal is not null) _signal.Changed -= OnChanged;
    }

    private static bool IsClosed(TaskItemDto entry) => entry.Status is EntryStatus.Done or EntryStatus.Archived;

    private void OnChanged()
    {
        Interlocked.Exchange(ref _dirty, 1);

        // One sweep after another, never two at once: each write queues a drain behind
        // the last, and a drain that finds nothing owed returns at once.
        lock (_chain)
        {
            _running = _running.ContinueWith(_ => DrainAsync(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    private async Task DrainAsync()
    {
        while (Interlocked.Exchange(ref _dirty, 0) == 1)
        {
            try
            {
                var closed = await SweepAsync().ConfigureAwait(false);

                if (closed > 0) _logger.LogInformation("Closed {Count} session requests whose work is finished.", closed);
            }
            catch (Exception exception)
            {
                // A sweep that fails leaves the queue as it was; the next write tries again.
                _logger.LogWarning(exception, "Could not close the session requests whose work is finished.");
            }
        }
    }
}
