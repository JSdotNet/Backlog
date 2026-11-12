using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// The Sessions context's answer to the subject probe
/// (<see cref="INotificationSubjectProbe"/>): whether a session, a delivery run or a pull
/// request a notification is about still lives, and why it ended, read from this context's
/// own sources so Notifications never reads them. One probe answers all three, because the
/// startup pass asks one probe per subject context; <see cref="RunSubjectWatch"/> asks it
/// about the runs open questions wait on while the app runs.
/// <list type="bullet">
/// <item>A <b>session</b> ended once the Claude desktop app archived it, or once no source
/// knows it at all. One without a desktop record — a CLI session, a Copilot one — is live,
/// because nothing says it ended.</item>
/// <item>A <b>run</b> ended once it left <c>in_progress</c>, or once no run file holds its
/// id in two readings: a run nobody will finish is abandoned. Asked twice because the run
/// reader skips a file it cannot read that moment, and the run being written is the one a
/// question is most likely about. A run still in progress also ended once every pull request
/// it recorded on its stages reads merged or closed (<see cref="RunPullRequests"/>, the set
/// the gate reads before it asks), or once every session it names is known and archived. A
/// session no source knows is not an end — the id is the agent's word, and one no reader
/// indexes says nothing — and neither is a session only stalled or reaped by the desktop
/// app: time alone never settles a question.</item>
/// <item>A <b>pull request</b> ended once merged or closed, as
/// <see cref="ISessionPullRequestStates"/> reads it; with no such port, or no answer, it
/// is live.</item>
/// </list>
/// A session reading that named an unreadable folder answers live for whatever it did not
/// find. Each reading is taken once and shared by the subjects asked about within a minute,
/// because the pass asks one subject at a time and a full session reading is not cheap; a
/// reading that failed is not kept.
/// </summary>
public sealed class SessionSubjectProbe : INotificationSubjectProbe
{
    private static readonly TimeSpan Reuse = TimeSpan.FromMinutes(1);

    private readonly IAgentSessionSource _sessions;
    private readonly IDeliveryRunSource _runs;
    private readonly ISessionPullRequestStates? _pullRequests;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();

    private (DateTimeOffset At, Task<AgentSessionCatalog> Reading)? _sessionReading;
    private (DateTimeOffset At, Task<DeliveryRunCatalog> Reading)? _runReading;

    public SessionSubjectProbe(
        IAgentSessionSource sessions,
        IDeliveryRunSource runs,
        ISessionPullRequestStates? pullRequests = null,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(runs);

        _sessions = sessions;
        _runs = runs;
        _pullRequests = pullRequests;
        _time = time ?? TimeProvider.System;
    }

    public string Context => SessionNotificationSubjects.Context;

    public async Task<bool> IsLiveAsync(NotificationSubject subject, CancellationToken cancellationToken = default) =>
        await EndAsync(subject, cancellationToken).ConfigureAwait(false) is null;

    public Task<string?> EndReasonAsync(NotificationSubject subject, CancellationToken cancellationToken = default) =>
        EndAsync(subject, cancellationToken);

    /// <summary>Why <paramref name="subject"/> ended, in the words its notifications close
    /// with, or null while it lives.</summary>
    private async Task<string?> EndAsync(NotificationSubject subject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);

        return subject.Type switch
        {
            SessionNotificationSubjects.Session => await SessionIsLiveAsync(subject.Id, cancellationToken).ConfigureAwait(false)
                ? null
                : SessionNotificationSubjects.SessionEndedReason,
            SessionNotificationSubjects.Run => await RunEndAsync(subject.Id, cancellationToken).ConfigureAwait(false),
            SessionNotificationSubjects.PullRequest => SessionNotificationSubjects.ParsePullRequestId(subject.Id) is { } pull && _pullRequests is not null
                ? RunPullRequests.EndOf(await _pullRequests.StateAsync(pull.Repository, pull.Number, cancellationToken).ConfigureAwait(false))
                : null,
            // A type this context does not name is nothing it can say ended.
            _ => null
        };
    }

    private async Task<bool> SessionIsLiveAsync(string id, CancellationToken cancellationToken)
    {
        var catalog = await Held(ref _sessionReading, ReadSessionsAsync, fresh: false).WaitAsync(cancellationToken).ConfigureAwait(false);
        var found = catalog.Sessions.Where(session => string.Equals(session.Id, id, StringComparison.Ordinal)).ToList();

        if (found.Count == 0) return catalog.Unreadable.Count > 0;

        return !found.Any(session => session.Archived is true);
    }

    private async Task<string?> RunEndAsync(string runId, CancellationToken cancellationToken)
    {
        var catalog = await Held(ref _runReading, ReadRunsAsync, fresh: false).WaitAsync(cancellationToken).ConfigureAwait(false);
        var run = Find(catalog, runId);

        if (run is null)
        {
            // Missing once may be a file the reader could not open just then.
            catalog = await Held(ref _runReading, ReadRunsAsync, fresh: true).WaitAsync(cancellationToken).ConfigureAwait(false);
            run = Find(catalog, runId);
        }

        if (run is null) return catalog.Unreadable.Count > 0 ? null : SessionNotificationSubjects.RunEndedReason;
        if (!run.InProgress) return SessionNotificationSubjects.RunEndedReason;

        // The pull request first: one read through the contract's cache, where the
        // session reading is the costly one.
        if (await PullRequestEndAsync(run, cancellationToken).ConfigureAwait(false) is { } ended) return ended;

        return await SessionsEndedAsync(run, cancellationToken).ConfigureAwait(false)
            ? SessionNotificationSubjects.RunEndedReason
            : null;

        static DeliveryRun? Find(DeliveryRunCatalog catalog, string runId) =>
            catalog.Runs.FirstOrDefault(candidate => candidate.RunIds.Contains(runId, StringComparer.Ordinal));
    }

    /// <summary>Why the pull requests the run recorded ended, once every one did; null
    /// while any is open or unread, or without the port.</summary>
    private Task<string?> PullRequestEndAsync(DeliveryRun run, CancellationToken cancellationToken) =>
        _pullRequests is null
            ? Task.FromResult<string?>(null)
            : RunPullRequests.EndAsync(RunPullRequests.Of(run), _pullRequests, cancellationToken);

    /// <summary>Whether the run names its sessions and every one of them is known and
    /// archived. A session missing from the reading keeps the run live: "never indexed" and
    /// "deleted" cannot be told apart, and only the first is the agent's mistake.</summary>
    private async Task<bool> SessionsEndedAsync(DeliveryRun run, CancellationToken cancellationToken)
    {
        if (run.SessionIds.Count == 0) return false;

        var catalog = await Held(ref _sessionReading, ReadSessionsAsync, fresh: false).WaitAsync(cancellationToken).ConfigureAwait(false);

        return run.SessionIds.All(id =>
            catalog.Sessions.Any(session => string.Equals(session.Id, id, StringComparison.Ordinal) && session.Archived is true));
    }

    /// <summary>Every session any source knows, with no cap: a session missing from a
    /// capped reading is only old, not gone.</summary>
    private Task<AgentSessionCatalog> ReadSessionsAsync() =>
        _sessions.GetSessionsAsync(AgentSessionQuery.Since(DateTimeOffset.MinValue).TimestampsOnly(), CancellationToken.None);

    private Task<DeliveryRunCatalog> ReadRunsAsync() => _runs.GetRunsAsync(CancellationToken.None);

    /// <summary>The reading held in <paramref name="slot"/> while it is under a minute old and
    /// has not failed, else a new one. Read with no caller's token, so one caller giving up
    /// does not hand every later caller a cancelled reading.</summary>
    private Task<T> Held<T>(ref (DateTimeOffset At, Task<T> Reading)? slot, Func<Task<T>> read, bool fresh)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();

            if (!fresh && slot is { } held && now - held.At < Reuse && !held.Reading.IsFaulted && !held.Reading.IsCanceled)
            {
                return held.Reading;
            }

            var reading = read();
            slot = (now, reading);
            return reading;
        }
    }
}
