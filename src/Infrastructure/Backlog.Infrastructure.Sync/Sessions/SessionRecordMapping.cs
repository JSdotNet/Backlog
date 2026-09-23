using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.Sessions;

/// <summary>
/// The sanitization boundary, as one pure function each way.
/// <para>
/// <strong>This is the whole reason a session record may leave the machine, and
/// it is deliberately the only place a record is built.</strong>
/// .arc42/adr/0005 §Session records states a whitelist of nineteen fields and says in
/// as many words that a whitelist and a filter fail in opposite directions: a
/// filter that misses a field leaks it, a whitelist that misses one merely omits
/// it. <see cref="SessionRecord"/> makes that structural — a field not in the
/// table does not exist on the type. This class is the second half of the same
/// property: one function, no state, no clock, no I/O, so "what leaves this
/// machine" is a question with exactly one place to read the answer.
/// </para>
/// <para>
/// <strong><see cref="AgentSession.WorkingFolder"/> never leaves.</strong> It is
/// a raw absolute path — it describes one machine's disk, means nothing on the
/// machine that read it, and .arc42/adr/0005 §Scope lists paths among the four
/// things that stay out precisely because the receiving machine would then act on
/// one. What travels in its place is the dashboards' one-way key for it, which is
/// what matching a delivery run needs and all it needs.
/// </para>
/// <para>
/// <strong><see cref="AgentSession.Title"/> leaves, by decision.</strong> An agent
/// derives it from what the person typed, so it is a fragment of a prompt, and it
/// was kept home on that ground until 2026-09-23. The owner reversed that in
/// .arc42/adr/0005 §Session records: the record is how a session stays
/// recognisable on another machine and after its transcript is gone, and an id is
/// not recognisable. It is cut to <see cref="SessionRecordLimits.TitleLength"/>.
/// </para>
/// </summary>
public static class SessionRecordMapping
{
    /// <summary>The token an agent kind travels as. Lower case, and opaque to the
    /// service by design (.arc42/adr/0005 §Storage: no domain logic runs against
    /// the replica).</summary>
    private const string ClaudeToken = "claude";

    /// <inheritdoc cref="ClaudeToken"/>
    private const string CopilotToken = "copilot";

    /// <summary>
    /// One local session as the wire carries it.
    /// <para>
    /// Every field is named explicitly rather than mapped by convention. A
    /// convention-based mapper would carry whatever the source record grew next,
    /// which is the filter failure mode this boundary exists to avoid.
    /// </para>
    /// </summary>
    /// <param name="session">A session this machine read for itself. Only a
    /// session with <see cref="AgentSessionOrigin.Local"/> may be pushed — see
    /// <see cref="SessionSyncSession"/>, which is where that is enforced, because
    /// it is a rule about who may write rather than about how a record is
    /// shaped.</param>
    /// <param name="aliases">Where a recorded <c>owner/name</c> becomes the alias
    /// this machine calls it.</param>
    /// <param name="activity">What this machine folded out of the session's own
    /// transcript, or null where it folded nothing — no parsable record, or none
    /// inside the horizon the push asked for. Null travels as null in both lists,
    /// which the far side reads as "no record"; a record travels as two lists, each
    /// cut to <see cref="SessionRecordLimits.IntervalsPerList"/> from the front.
    /// The caller has already established that this is this machine's own record
    /// (<see cref="AgentSessionActivity.Origin"/>), for the reason the session
    /// itself has to be local: that is a rule about who may write, and it is
    /// enforced where the writing is decided.</param>
    public static SessionRecord ToRecord(AgentSession session, ISessionRepositoryAliases aliases, AgentSessionActivity? activity)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(aliases);

        return new SessionRecord(
            session.Id,
            TokenFor(session.Kind),
            session.Environment,
            RepositoryAliasFor(session.Repository, aliases),
            session.Branch,
            session.StartedAt,
            session.LastActivityAt,
            session.TurnCount,
            DurationSecondsOf(session.StartedAt, session.LastActivityAt),
            // The same alias-or-owner/name rule as the recorded repository, and
            // the same reason: a machine without the alias configured still sends
            // a true statement rather than nothing. What is never done here is
            // resolving one from the other — a record that recorded a repository
            // and resolved none, or the reverse, goes out exactly so.
            RepositoryAliasFor(session.ResolvedRepository, aliases),
            activity is null ? null : Newest(activity.Runs.Select(run => new ActivityInterval(run.StartedAt, run.EndedAt)), SessionRecordLimits.IntervalsPerList),
            activity is null ? null : Newest(activity.Waits.Select(wait => new ActivityInterval(wait.StartedAt, wait.EndedAt)), SessionRecordLimits.IntervalsPerList),
            Cut(session.Title, SessionRecordLimits.TitleLength),
            Cut(DeliveryRunWorktrees.KeyOf(session.WorkingFolder), SessionRecordLimits.WorktreeKeyLength),
            activity is null ? null : Newest(activity.LimitHits.OrderBy(hit => hit.At).Select(HitRecordOf), SessionRecordLimits.LimitHitsPerList),
            Cut(session.Entrypoint, SessionRecordLimits.LimitTokenLength),
            session.PullRequests is null ? null
                : Newest(
                    session.PullRequests
                        .Where(pr => pr.Url.Length <= SessionRecordLimits.UrlLength && pr.Repository.Length <= SessionRecordLimits.RepositoryLength)
                        .Select(pr => new PullRequestRecord(pr.Repository, pr.Number, pr.Url, pr.LinkedAt)),
                    SessionRecordLimits.PullRequestsPerList),
            session.ModelUsage is null ? null
                : [.. session.ModelUsage
                    .OrderByDescending(usage => usage.OutputTokens)
                    .Take(SessionRecordLimits.ModelsPerList)
                    .Select(usage => new ModelUsageRecord(
                        Cut(usage.Model, SessionRecordLimits.LimitTokenLength)!,
                        usage.InputTokens,
                        usage.OutputTokens,
                        usage.CacheCreationInputTokens,
                        usage.CacheReadInputTokens))]);
    }

    /// <summary>One refusal as the wire carries it, every token cut to what the
    /// service accepts so a longer one than this build has seen cannot get the whole
    /// record refused on every cycle.</summary>
    private static LimitHitRecord HitRecordOf(AgentLimitHit hit) => new(
        hit.At,
        hit.Kind.ToString(),
        Cut(hit.RateLimitType, SessionRecordLimits.LimitTokenLength),
        hit.ResetsAt,
        Cut(hit.OverageStatus, SessionRecordLimits.LimitTokenLength),
        hit.OverageResetsAt,
        Cut(hit.OverageDisabledReason, SessionRecordLimits.LimitTokenLength),
        hit.IsUsingOverage);

    /// <summary>One refusal back in the Sessions context's type. A kind this build
    /// does not know is Other rather than a throw — the raw type beside it is still
    /// the fact.</summary>
    private static AgentLimitHit HitOf(LimitHitRecord hit) =>
        new(
            hit.At,
            Enum.TryParse<AgentLimitKind>(hit.Kind, ignoreCase: false, out var kind) && Enum.IsDefined(kind) ? kind : AgentLimitKind.Other,
            hit.RateLimitType)
        {
            ResetsAt = hit.ResetsAt,
            OverageStatus = hit.OverageStatus,
            OverageResetsAt = hit.OverageResetsAt,
            OverageDisabledReason = hit.OverageDisabledReason,
            IsUsingOverage = hit.IsUsingOverage
        };

    /// <summary>A blank value as null, and a long one cut to the cap.</summary>
    private static string? Cut(string? value, int length) =>
        string.IsNullOrWhiteSpace(value) ? null
            : value.Length <= length ? value
            : value[..length];

    /// <summary>
    /// One record's activity from another environment, as this device's activity
    /// port answers it, or null where there is nothing to answer.
    /// <para>
    /// Null when both lists are null — the pushing machine had no record, and the
    /// rule the local source reads its own folders under is that such a session is
    /// absent rather than present-and-empty. Null again when nothing survives the
    /// horizon, for the same reason: a record with two empty lists would be a
    /// session claiming to have been measured inside a window it was never in.
    /// An empty list that arrived empty is neither of those; a Copilot session
    /// with runs and no waits is a record, and it is answered as one.
    /// </para>
    /// <para>
    /// Clipped to <paramref name="since"/> the way <c>LocalAgentActivitySource</c>
    /// clips its own: an interval that ended on or before the horizon goes, and one
    /// that straddles it starts at the horizon. The two sources have to agree on
    /// this or a machine's figure would depend on which of them measured it.
    /// </para>
    /// <para>
    /// Sorted ascending rather than trusted. The contract promises ordered,
    /// disjoint, ascending lists, and this device wrote none of these — the wire
    /// and the store between it and the pusher are not places that promise is
    /// known to have been kept. Sorting a few hundred timestamps costs nothing, and
    /// a sweep over an unsorted list would count some hours twice and others not at
    /// all. Validity is not re-checked here: the service refuses an interval that
    /// does not run forward, so nothing in the replica carries one.
    /// </para>
    /// </summary>
    /// <param name="entry">What came back from the feed, machine id and all.</param>
    /// <param name="since">The horizon the activity read was asked for.</param>
    /// <param name="environmentId">The environment to stamp in place of the record's
    /// machine id, or null to use it — see <see cref="ToSession"/>.</param>
    public static AgentSessionActivity? ToActivity(SessionRecordEntry entry, DateTimeOffset since, string? environmentId = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var record = entry.Record;

        if (record.Runs is null && record.Waits is null) return null;

        var runs = Clip(record.Runs ?? [], since, (start, end) => new AgentActivityRun(start, end));
        var waits = Clip(record.Waits ?? [], since, (start, end) => new AgentActivityWait(start, end));

        if (runs.Count == 0 && waits.Count == 0) return null;

        var hits = (record.LimitHits ?? [])
            .Where(hit => hit.At >= since)
            .OrderBy(hit => hit.At)
            .Select(HitOf)
            .ToList();

        return new AgentSessionActivity(
            record.SessionId,
            KindFor(record.AgentKind),
            // The Guid's plain "D" form, which is how ToSession spells the same id
            // and how the local sources spell theirs: an activity record and a
            // session row are the same machine by identity only while every source
            // writes the id one way.
            environmentId ?? entry.MachineId.ToString(),
            record.MachineName,
            runs,
            waits)
        {
            Origin = AgentSessionOrigin.Replicated,
            LimitHits = hits
        };
    }

    /// <summary>
    /// The last <see cref="SessionRecordLimits.IntervalsPerList"/> of a list that
    /// is longer than that, or the list itself.
    /// <para>
    /// The newest rather than the oldest, because a reader's window covers the end
    /// of a record before its beginning: the Dashboard sweeps the last seven days
    /// or the last twelve weeks, and a session long enough to overrun the cap is
    /// one whose oldest stretches are the first to fall out of any window. The
    /// list is taken in the order the contract promises — ascending — so the tail
    /// is the newest; this is an in-process record from a source this build owns,
    /// which is the one place that promise can be taken at its word.
    /// </para>
    /// </summary>
    private static IReadOnlyList<T> Newest<T>(IEnumerable<T> items, int cap)
    {
        var all = items.ToList();

        return all.Count <= cap ? all : all.GetRange(all.Count - cap, cap);
    }

    /// <summary>Ascending, clipped to the horizon, and in the consumer's own
    /// type. One shape for both lists, because the two differ only in what they
    /// are called.</summary>
    private static IReadOnlyList<T> Clip<T>(
        IReadOnlyList<ActivityInterval> intervals,
        DateTimeOffset since,
        Func<DateTimeOffset, DateTimeOffset, T> interval) =>
    [
        .. intervals
            .Where(candidate => candidate.EndedAt > since)
            .OrderBy(candidate => candidate.StartedAt)
            .Select(candidate => interval(candidate.StartedAt >= since ? candidate.StartedAt : since, candidate.EndedAt))
    ];

    /// <summary>
    /// One record from another environment, as this device's read model holds it.
    /// </summary>
    /// <param name="entry">What came back from the feed, machine id and all.</param>
    /// <param name="now">The clock the state is derived against. Passed in rather
    /// than read, which is what makes the staleness boundary testable at all.</param>
    /// <param name="environmentId">
    /// The environment to stamp in place of the record's machine id, or null to use
    /// it. Set for this machine's own records: the service issued the machine id when
    /// the device paired, and the local readers stamp the installation's own identity,
    /// so without this an archived session of this machine's would group under a
    /// second environment with the same name.
    /// </param>
    public static AgentSession ToSession(SessionRecordEntry entry, DateTimeOffset now, string? environmentId = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var record = entry.Record;

        return new AgentSession(
            record.SessionId,
            KindFor(record.AgentKind),
            environmentId ?? entry.MachineId.ToString(),
            record.MachineName,
            // The title the origin machine sent, and the session id from a device
            // that predates the field: that is what the record actually carries
            // that names the session to a person. Anything prettier would be this
            // device composing a description of work it never saw.
            string.IsNullOrWhiteSpace(record.Title) ? record.SessionId : record.Title,
            // There is no working folder on the wire and there could not be one:
            // a path from another machine describes a disk this one cannot see.
            string.Empty,
            record.RepositoryAlias,
            record.Branch,
            record.StartedAt,
            record.LastActivityAt,
            // Derived on read, never carried. .domain/sessions/domain.md puts a
            // session's state at "derived from the evidence available, never
            // asserted"; a state field on the wire would freeze the sender's
            // reading of its own clock and go on asserting "Running" for a session
            // that ended before the last sync.
            //
            // And the evidence here is one timestamp, which is why this is not
            // AgentSessionStates.Of. That helper answers Running or Stalled and
            // never Finished, because it exists for a live marker — a file an agent
            // writes while it is running, and stops writing when it is not. A
            // replicated record is not that. It is a record, and the same record
            // arrives whether the session is still going or ended a month ago, so
            // Of would read every replicated row as live for ever and the Live view
            // would fill with sessions that finished before the last sync.
            //
            // So the rule is CopilotSessionReader's, for the reason that reader
            // gives: with no liveness marker, silence past the threshold is the
            // only evidence there is, and it says Finished rather than Stalled.
            // Stalled is a claim that something is still there and quiet, and
            // nothing here can tell that from something that is gone.
            now - record.LastActivityAt > AgentSessionStates.StaleAfter
                ? AgentSessionState.Finished
                : AgentSessionState.Running,
            record.TurnCount,
            AgentSessionOrigin.Replicated)
        {
            // Held as it arrived. There is no folder on the wire to resolve it
            // from again, and the origin machine was the only one that ever had
            // both the folder and the clone it lay under.
            ResolvedRepository = record.ResolvedRepositoryAlias,
            // The folder's key in place of the folder, so a delivery run filed under
            // it can still find this session.
            WorktreeKey = record.WorktreeKey,
            Entrypoint = record.Entrypoint,
            PullRequests = record.PullRequests is null ? null
                : [.. record.PullRequests.Select(pr => new AgentPullRequest(pr.Repository, pr.Number, pr.Url, pr.LinkedAt))],
            ModelUsage = record.ModelUsage is null ? null
                : [.. record.ModelUsage.Select(usage => new AgentModelUsage(usage.Model, usage.InputTokens, usage.OutputTokens, usage.CacheCreationInputTokens, usage.CacheReadInputTokens))]
        };
    }

    /// <summary>
    /// The opaque token for an agent kind. One place converts, and
    /// <see cref="KindFor"/> is its inverse in the same file, so the two cannot be
    /// edited apart.
    /// </summary>
    public static string TokenFor(AgentSessionKind kind) => kind switch
    {
        AgentSessionKind.Copilot => CopilotToken,
        _ => ClaudeToken
    };

    /// <summary>
    /// The agent kind a token names.
    /// <para>
    /// A token this build does not recognise reads as
    /// <see cref="AgentSessionKind.Claude"/> rather than throwing, because the
    /// alternative is worse than a mislabelled row: one record written by a newer
    /// device that learned a third assistant would otherwise take down the whole
    /// page it arrived in, and the cursor past it has already been saved. The
    /// enum is the thing that has to widen for a third assistant, and until it
    /// does there is no honest answer here — only a loud failure or a quiet
    /// default, and the quiet default keeps the other machine's sessions visible.
    /// </para>
    /// </summary>
    public static AgentSessionKind KindFor(string token) =>
        string.Equals(token, CopilotToken, StringComparison.OrdinalIgnoreCase)
            ? AgentSessionKind.Copilot
            : AgentSessionKind.Claude;

    /// <summary>
    /// The repository as the wire carries it: this machine's alias where it has
    /// one, and the recorded <c>owner/name</c> where it does not.
    /// <para>
    /// <strong>Never null where a repository was recorded, and never derived from
    /// a working folder.</strong> .domain/sessions/domain.md is explicit that a
    /// repository guessed from a folder path is indistinguishable from a recorded
    /// one and wrong, and the receiving machine has no way to tell the two apart
    /// — so a guess made here would be believed there. Null on the wire means the
    /// agent recorded no repository, and it means only that. The resolved
    /// repository travels in its own field for exactly this reason: it is placed
    /// by the reading source against a registered clone, and it stays tellable
    /// apart from the recorded one all the way to the other machine.
    /// </para>
    /// <para>
    /// Falling back to <c>owner/name</c> rather than to null is the other half of
    /// the same rule. An alias is a label this machine happens to have configured;
    /// a repository the agent recorded is a fact about the session, and dropping
    /// the fact because the label is missing would lose it on every machine that
    /// has not been through the Repositories screen.
    /// </para>
    /// </summary>
    private static string? RepositoryAliasFor(string? repository, ISessionRepositoryAliases aliases) =>
        string.IsNullOrWhiteSpace(repository)
            ? null
            : aliases.AliasFor(repository) ?? repository;

    /// <summary>
    /// How long the session has been running, in whole seconds.
    /// <para>
    /// Derived here and nowhere else on this side, because the model deliberately
    /// does not carry it: <c>AgentSession</c>'s own doc comment argues that a
    /// stored duration can disagree with the two timestamps it was computed from.
    /// The wire carries it only because .arc42/adr/0005's whitelist names it and
    /// the relaying service is deliberately dumb about the fields it forwards —
    /// it cannot subtract two of them. Nothing reads it back: <see cref="ToSession"/>
    /// leaves it on the floor and the app derives duration from the timestamps, so
    /// a record whose duration disagreed with its own stamps would be corrected on
    /// arrival rather than believed.
    /// </para>
    /// <para>
    /// Zero where the agent recorded no start, which is the one case where zero is
    /// not a claim: with no start there is no interval, and the field is required.
    /// Zero rather than negative for a clock that went backwards between the two
    /// readings, for the same reason.
    /// </para>
    /// </summary>
    private static long DurationSecondsOf(DateTimeOffset? startedAt, DateTimeOffset lastActivityAt)
    {
        if (startedAt is not { } start) return 0;

        var seconds = (long)(lastActivityAt - start).TotalSeconds;

        return seconds < 0 ? 0 : seconds;
    }
}
