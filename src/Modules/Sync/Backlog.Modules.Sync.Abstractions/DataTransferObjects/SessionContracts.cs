namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>
/// One stretch of a session's time as it crosses the wire: the agent was producing
/// from <paramref name="StartedAt"/> until <paramref name="EndedAt"/>, or it had
/// stopped and nothing had prompted it yet. Which of the two it is comes from the
/// list it travels in — <see cref="SessionRecord.Runs"/> or
/// <see cref="SessionRecord.Waits"/> — and not from the interval itself.
/// <para>
/// Half-open, and <paramref name="EndedAt"/> is strictly after
/// <paramref name="StartedAt"/>; the service refuses a record carrying one that is
/// not, because a zero-length or inverted interval is not a stretch of anything.
/// </para>
/// <para>
/// Its own type rather than the Sessions context's <c>AgentActivityRun</c>, because
/// this assembly cannot reference that one — the wire contract is what the service
/// compiles against, and the service does not carry the desktop's session model.
/// Two timestamps and nothing else, which is the whole of what makes it safe to
/// send: an interval says <em>when</em> an agent was busy and nothing about what it
/// was busy with.
/// </para>
/// </summary>
public sealed record ActivityInterval(DateTimeOffset StartedAt, DateTimeOffset EndedAt);

/// <summary>How much of a session's activity one record may carry.</summary>
public static class SessionRecordLimits
{
    /// <summary>
    /// The most intervals either list on a <see cref="SessionRecord"/> may hold.
    /// <para>
    /// Shared between the two ends on <see cref="PairingCodeFormat.Length"/>'s
    /// precedent, because this is the one bound where the pusher's number has to be
    /// <em>exactly</em> the service's rather than comfortably below it: the pusher
    /// truncates to this cap and the service refuses above it, and two copies that
    /// drifted by one would have every busy session refused on every cycle for ever.
    /// </para>
    /// <para>
    /// Five hundred is generous for a session. The fold that produces these closes
    /// a run after five minutes of silence, so a session with five hundred runs is
    /// one that went quiet five hundred times — a long day of work lands well under
    /// it, and what a longer one loses is its oldest stretches, which the pusher
    /// drops in favour of the newest.
    /// </para>
    /// </summary>
    public const int IntervalsPerList = 500;

    /// <summary>The most limit hits one record may carry. A refusal is followed by
    /// silence until the allowance resets, so a session that met a hundred walls has
    /// been refused far more often than any week allows; the newest are kept.</summary>
    public const int LimitHitsPerList = 100;

    /// <summary>The longest title a record may carry. The pusher cuts to this and
    /// the service refuses above it, on the terms <see cref="IntervalsPerList"/> gives.</summary>
    public const int TitleLength = 500;

    /// <summary>The longest worktree key a record may carry: a folder leaf, a hyphen
    /// and eight hex characters.</summary>
    public const int WorktreeKeyLength = 300;

    /// <summary>The longest of the free-text fields on a <see cref="LimitHitRecord"/>
    /// — the kind, the raw bucket, the overage status and reason. Each is a token
    /// the assistant wrote, and none is anywhere near this.</summary>
    public const int LimitTokenLength = 100;

    /// <summary>The longest repository name on a pull request.</summary>
    public const int RepositoryLength = 200;

    /// <summary>The most pull requests one record may carry, newest kept.</summary>
    public const int PullRequestsPerList = 100;

    /// <summary>The most models one record may carry a usage line for.</summary>
    public const int ModelsPerList = 20;

    /// <summary>The longest pull request URL a record may carry.</summary>
    public const int UrlLength = 500;
}

/// <summary>
/// A pull request a session linked itself to, as it crosses the wire: which repository,
/// which number, where, and when the session linked it. Its own type for the reason
/// <see cref="ActivityInterval"/> gives.
/// </summary>
public sealed record PullRequestRecord(string Repository, int Number, string Url, DateTimeOffset? LinkedAt = null);

/// <summary>
/// What a session spent on one model — the assistant's own token counts, summed over the
/// session's own messages. Counts, never content.
/// </summary>
public sealed record ModelUsageRecord(
    string Model,
    long InputTokens,
    long OutputTokens,
    long CacheCreationInputTokens,
    long CacheReadInputTokens);

/// <summary>
/// One moment an agent was refused for a usage limit, as it crosses the wire. The
/// Sessions context's <c>AgentLimitHit</c> in a type this assembly can own, for the
/// reason <see cref="ActivityInterval"/> gives.
/// <para>
/// Instants and the tokens the assistant wrote beside them, and nothing else: which
/// bucket refused, when it would reset, and what it said about paid overage. No
/// prompt, and not the refusal's own sentence.
/// </para>
/// </summary>
/// <param name="At">When the refusal was recorded.</param>
/// <param name="Kind">The kind the pushing machine mapped it to — <c>FiveHour</c>,
/// <c>Weekly</c>, <c>WeeklyFable</c> or <c>Other</c> — as the enum member's name,
/// so a reordered enum cannot relabel a stored hit. Carried beside the raw type
/// because an older transcript named its limit only in prose, and the pusher is
/// the only machine that read it.</param>
/// <param name="RateLimitType">The bucket exactly as the transcript spelled it, or
/// null where it spelled none.</param>
/// <param name="ResetsAt">When the refused allowance resets, or null.</param>
/// <param name="OverageStatus">What the refusal said about overage, or null.</param>
/// <param name="OverageResetsAt">When the overage allowance resets, or null.</param>
/// <param name="OverageDisabledReason">Why overage was unavailable, or null.</param>
/// <param name="IsUsingOverage">Whether overage was already being spent, or null.</param>
public sealed record LimitHitRecord(
    DateTimeOffset At,
    string Kind,
    string? RateLimitType = null,
    DateTimeOffset? ResetsAt = null,
    string? OverageStatus = null,
    DateTimeOffset? OverageResetsAt = null,
    string? OverageDisabledReason = null,
    bool? IsUsingOverage = null);

/// <summary>
/// One coding-agent session as it crosses the wire — the whole of what
/// .devbook/arc42/adr/0005 §Session records permits to leave the machine, and nothing
/// else.
/// <para>
/// <strong>This is a whitelist, not a filter, and the record type is where that
/// distinction becomes structural.</strong> The two fail in opposite directions:
/// a filter that misses a field leaks it, a whitelist that misses one merely
/// omits it. There are nineteen permitted fields in that record's table — eighteen
/// here and the machine id the service stamps — and a field that is not in the
/// table does not exist on this type. Never a working folder, never a transcript
/// path, and never a prompt, a tool result or a line of a file the session read.
/// The title is the one exception to the prompt rule, taken on 2026-09-23 and
/// argued there. Widening the table is a decision taken in that record, not a
/// property added here.
/// </para>
/// <para>
/// <see cref="AgentKind"/> is an opaque string and deliberately not an enum, for
/// the same reason <c>TaskPayload.Type</c> and <c>TaskPayload.Status</c> are
/// strings: it carries whatever token the device writes — <c>claude</c>,
/// <c>copilot</c> — and the service never parses one. An enum here would have to
/// be redeployed the day the desktop learns a third assistant, and
/// .devbook/arc42/adr/0005 §Storage says no domain logic runs against the replica. It
/// still travels, because .devbook/domain/sessions/domain.md#session-identity puts a
/// session's identity at the agent plus the id that agent issued: a record
/// arriving without its agent would let the receiving log merge two unrelated
/// sessions, which is the one failure that rule exists to prevent.
/// </para>
/// <para>
/// <strong>There is no machine id and no owner id on this record, and that
/// absence is the security property</strong> — the same one
/// <see cref="TaskChange"/> relies on. Both come from the caller's validated
/// token (.devbook/arc42/adr/0005 §Identity), so there is no batch a client could
/// compose that writes a record attributed to another machine. That is what
/// makes §Session records' rule — <em>"a caller may only write records stamped
/// with its own machine id"</em> — hold by construction rather than by a check
/// somebody could forget to write, or write and later delete.
/// <see cref="SessionRecordEntry"/> is where the service adds the machine id
/// back on the way out.
/// </para>
/// <para>
/// The three nullable scalars are genuinely unknown rather than merely absent. A
/// session may have been started outside any repository, may be on a detached
/// head, and may not have had a start time recorded by the agent that ran it;
/// none of the three is a reason to withhold the record. The two nullable lists
/// are a different kind of null — see <paramref name="Runs"/>.
/// </para>
/// </summary>
/// <param name="SessionId">The identifier the agent gave the session. Unique
/// only within that agent, which is why <paramref name="AgentKind"/> travels
/// beside it.</param>
/// <param name="AgentKind">Which assistant ran it, as an opaque token.</param>
/// <param name="MachineName">What the operating system calls the environment
/// that ran it. A display label, never the identity — the machine id is that,
/// and it is stamped by the service.</param>
/// <param name="RepositoryAlias">The repository's alias, never its path. A path
/// describes one machine's disk and would mean nothing, or something wrong, on
/// the machine that read it.</param>
/// <param name="Branch">The branch the session worked on.</param>
/// <param name="StartedAt">When the session began, if the agent recorded
/// it.</param>
/// <param name="LastActivityAt">When it was last seen alive.</param>
/// <param name="TurnCount">
/// How many turns it has taken, or null where the agent recorded nothing to count
/// from.
/// <para>
/// Nullable, and <c>0</c> is deliberately not the stand-in for "not recorded".
/// <c>.devbook/domain/sessions/domain.md#session-log</c> holds that the log never fills a
/// gap the agent left, and <c>0</c> is not a gap — it is a count, and it says a
/// person opened a session and never spoke in it. Copilot records no turn count at
/// all, so mapping absence onto <c>0</c> would have every Copilot record on every
/// other machine asserting something that never happened. A sentinel would do the
/// same job and worse: it reads as a number to anything that has not been told
/// otherwise, and the whole point is that this is not a number.
/// </para>
/// </param>
/// <param name="DurationSeconds">How long it has been running.</param>
/// <param name="ResolvedRepositoryAlias">
/// The repository the pushing machine placed the session in from its working
/// folder lying inside a registered clone — the alias where that machine has
/// one, the <c>owner/name</c> otherwise — or null where no registered clone
/// contained the folder. The eleventh whitelisted field, added to
/// .devbook/arc42/adr/0005 §Session records on 2026-09-22.
/// <para>
/// Beside <paramref name="RepositoryAlias"/> rather than folded into it. That
/// field is what the agent wrote and this is what the product worked out, and
/// a receiving machine cannot tell the two apart once they share a slot —
/// which is the failure <c>.devbook/domain/sessions/domain.md#working-location</c>
/// names. It is still not a path: the folder itself never leaves, only which
/// registered clone it was under.
/// </para>
/// <para>
/// Defaulted, so a record written by a device that predates it deserialises with
/// the field absent rather than failing the page it arrived in — the same reason
/// <see cref="TurnCount"/> is nullable rather than zero. The two lists after it
/// are defaulted on the same terms.
/// </para>
/// </param>
/// <param name="Runs">
/// The stretches in which the agent was producing, ascending, or null.
/// <para>
/// <strong>Timestamps about work, never the work.</strong> A run says an agent was
/// busy from one instant to another and carries no prompt, no tool result and no
/// file — which is the same distinction the whitelist draws for every other field
/// and the reason these two may cross it at all. They travel because the reading
/// device cannot compute them: utilisation is folded out of a transcript's body,
/// and the transcript stays home. Without these a session from another machine is
/// a row the Dashboard can count and cannot measure, and its machine reads as
/// having done nothing all week.
/// </para>
/// <para>
/// <strong>Null and empty mean different things, and both survive the trip.</strong>
/// Null says the pushing machine had no parsable activity record for the session
/// — an older client, a transcript it could not open, a session that left one
/// lone event — and the reader counts that as "no record", the same way it counts
/// a local session whose file it could not fold. An empty list says the record was
/// read and this is what it held: every Copilot session travels with empty waits,
/// because Copilot cannot mark that boundary, and that is a fact about the session
/// rather than a gap in the record. Collapsing the two would have every Copilot
/// session on every other machine reading as unrecorded.
/// </para>
/// <para>
/// At most <see cref="SessionRecordLimits.IntervalsPerList"/>, and the newest
/// ones when there were more: the service refuses a longer list rather than
/// trimming it, and the pusher keeps the end of the record because that is the
/// end a reader's window covers. Default null, so a client built before these two
/// fields existed produces the record it always did.
/// </para>
/// </param>
/// <param name="Waits">The stretches in which the agent had stopped and nothing
/// had prompted it yet, on the same terms as <paramref name="Runs"/>. Empty
/// rather than null for every Copilot session.</param>
/// <param name="Title">
/// What the session is called in a list — the agent's own name for it where it
/// wrote one, the folder's leaf or a short id otherwise — or null from a device
/// that predates the field. Added to .devbook/arc42/adr/0005 §Session records on
/// 2026-09-23, reversing the rule that kept it home: the owner decided that a
/// session they can recognise on their other machine, and after its transcript
/// is gone on this one, is worth a line of prompt-derived text in their own
/// replica. Capped at <see cref="SessionRecordLimits.TitleLength"/>.
/// </param>
/// <param name="WorktreeKey">
/// The delivery dashboards' key for the session's working folder — the folder's
/// leaf and eight hex characters of a one-way hash of its path — or null where
/// the session had no folder. Added on 2026-09-23 so a session whose folder does
/// not travel can still be matched to the delivery runs filed under it. It names
/// the folder's leaf and nothing above it; the path itself still never leaves.
/// </param>
/// <param name="LimitHits">
/// The moments the agent was refused for a usage limit, ascending, or null on the
/// terms <paramref name="Runs"/> is null. Added on 2026-09-23: a limit belongs to
/// the account rather than the machine, so a refusal on one machine is a fact
/// about the week on every other. At most <see cref="SessionRecordLimits.LimitHitsPerList"/>,
/// the newest kept.
/// </param>
/// <param name="Entrypoint">Where the agent was run from — <c>claude-desktop</c>,
/// <c>cli</c> — or null. Added on 2026-09-23.</param>
/// <param name="PullRequests">The pull requests the session linked itself to, or null
/// where the pusher could not say. Added on 2026-09-23.</param>
/// <param name="ModelUsage">What the session spent per model, or null on the same terms.
/// Added on 2026-09-23.</param>
public sealed record SessionRecord(
    string SessionId,
    string AgentKind,
    string MachineName,
    string? RepositoryAlias,
    string? Branch,
    DateTimeOffset? StartedAt,
    DateTimeOffset LastActivityAt,
    int? TurnCount,
    long DurationSeconds,
    string? ResolvedRepositoryAlias = null,
    IReadOnlyList<ActivityInterval>? Runs = null,
    IReadOnlyList<ActivityInterval>? Waits = null,
    string? Title = null,
    string? WorktreeKey = null,
    IReadOnlyList<LimitHitRecord>? LimitHits = null,
    string? Entrypoint = null,
    IReadOnlyList<PullRequestRecord>? PullRequests = null,
    IReadOnlyList<ModelUsageRecord>? ModelUsage = null);

/// <summary>
/// A session record as it comes back out of the replica: the record itself, the
/// machine that wrote it, and the store's own ordering stamp.
/// <para>
/// <paramref name="MachineId"/> is the whitelisted field the pushing device never
/// sends. It lets a client tell its own records from the other machines' — it keeps
/// both, and answers from its own only for a session whose transcript it can no
/// longer read — and it is what a reading device groups by —
/// .devbook/domain/sessions/domain.md#environment keys an environment on its id and not
/// on the name it displays, because a name can be shared by two machines and
/// changed on one.
/// </para>
/// <para>
/// <paramref name="ServerTimestamp"/> is the store's stamp rather than a clock
/// any device controls, so two machines with skewed clocks still agree on the
/// order the service saw. Session records are single-writer and have no
/// last-write-wins tie to break (.devbook/arc42/adr/0005 §Session records); the stamp
/// orders the feed, and that is all it is for.
/// </para>
/// </summary>
public sealed record SessionRecordEntry(SessionRecord Record, Guid MachineId, long ServerTimestamp);

/// <summary>A batch of session records from one machine. Batched because a
/// machine that has been offline all day has a day of them, and because the
/// desktop pushes on a timer rather than per turn.</summary>
public sealed record PushSessionsRequest(IReadOnlyList<SessionRecord> Sessions);

/// <summary>
/// How many records the replica took.
/// <para>
/// There is no per-item result, and for a different reason than the task push
/// has none. A task push has nothing to reject because the whole document is the
/// unit of last-write-wins; a session push has nothing to reject because the
/// records were already validated whole at the edge — an invalid batch is
/// refused entirely and never reaches the replica, so a partial count could only
/// ever mean the store failed, and that arrives as a problem body rather than as
/// a number.
/// </para>
/// </summary>
public sealed record PushSessionsResponse(int Accepted);

/// <summary>
/// One page of the owner's session feed, and where to resume.
/// <para>
/// <paramref name="Since"/> is always the position *after* this page, including
/// on a drained feed — the feed's position advances whether or not the page had
/// anything in it, and a client that kept its old cursor would re-scan from there
/// forever. It is not promised to be a *different* string each time: pulling an
/// already-drained feed twice returns the same cursor, because the position did
/// not move. Resume from whatever came back and do not treat an unchanged cursor
/// as a fault.
/// <paramref name="HasMore"/> is the service's own "there is more of your feed
/// after this page", and false means the store has said the feed is drained
/// rather than that the page came back short. A client keeps pulling until it is
/// false, and may not stop early on a page smaller than it asked for — a page
/// size is a hint to the store, not a promise.
/// </para>
/// </summary>
public sealed record PullSessionsResponse(IReadOnlyList<SessionRecordEntry> Sessions, string Since, bool HasMore);
