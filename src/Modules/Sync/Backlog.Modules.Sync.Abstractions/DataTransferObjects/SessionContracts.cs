namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>
/// One coding-agent session as it crosses the wire — the whole of what
/// .arc42/adr/0005 §Session records permits to leave the machine, and nothing
/// else.
/// <para>
/// <strong>This is a whitelist, not a filter, and the record type is where that
/// distinction becomes structural.</strong> The two fail in opposite directions:
/// a filter that misses a field leaks it, a whitelist that misses one merely
/// omits it. There are ten permitted fields in that record's table — nine here
/// and the machine id the service stamps — and a field that is not in the table
/// does not exist on this type. Never a working folder, never a title, never a
/// transcript path, and never a prompt, a tool result or a line of a file the
/// session read. Widening the table is a decision taken in that record, not a
/// property added here.
/// </para>
/// <para>
/// <see cref="AgentKind"/> is an opaque string and deliberately not an enum, for
/// the same reason <c>TaskPayload.Type</c> and <c>TaskPayload.Status</c> are
/// strings: it carries whatever token the device writes — <c>claude</c>,
/// <c>copilot</c> — and the service never parses one. An enum here would have to
/// be redeployed the day the desktop learns a third assistant, and
/// .arc42/adr/0005 §Storage says no domain logic runs against the replica. It
/// still travels, because .domain/sessions/naming.md#session-identity puts a
/// session's identity at the agent plus the id that agent issued: a record
/// arriving without its agent would let the receiving log merge two unrelated
/// sessions, which is the one failure that rule exists to prevent.
/// </para>
/// <para>
/// <strong>There is no machine id and no owner id on this record, and that
/// absence is the security property</strong> — the same one
/// <see cref="TaskChange"/> relies on. Both come from the caller's validated
/// token (.arc42/adr/0005 §Identity), so there is no batch a client could
/// compose that writes a record attributed to another machine. That is what
/// makes §Session records' rule — <em>"a caller may only write records stamped
/// with its own machine id"</em> — hold by construction rather than by a check
/// somebody could forget to write, or write and later delete.
/// <see cref="SessionRecordEntry"/> is where the service adds the machine id
/// back on the way out.
/// </para>
/// <para>
/// The three nullable members are genuinely unknown rather than merely absent. A
/// session may have been started outside any repository, may be on a detached
/// head, and may not have had a start time recorded by the agent that ran it;
/// none of the three is a reason to withhold the record.
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
/// <c>.domain/sessions/domain.md#session-log</c> holds that the log never fills a
/// gap the agent left, and <c>0</c> is not a gap — it is a count, and it says a
/// person opened a session and never spoke in it. Copilot records no turn count at
/// all, so mapping absence onto <c>0</c> would have every Copilot record on every
/// other machine asserting something that never happened. A sentinel would do the
/// same job and worse: it reads as a number to anything that has not been told
/// otherwise, and the whole point is that this is not a number.
/// </para>
/// </param>
/// <param name="DurationSeconds">How long it has been running.</param>
public sealed record SessionRecord(
    string SessionId,
    string AgentKind,
    string MachineName,
    string? RepositoryAlias,
    string? Branch,
    DateTimeOffset? StartedAt,
    DateTimeOffset LastActivityAt,
    int? TurnCount,
    long DurationSeconds);

/// <summary>
/// A session record as it comes back out of the replica: the record itself, the
/// machine that wrote it, and the store's own ordering stamp.
/// <para>
/// <paramref name="MachineId"/> is the tenth whitelisted field and the one the
/// pushing device never sends. It lets a client drop its own echo instead of
/// re-applying what it just pushed, and it is what a reading device groups by —
/// .domain/sessions/naming.md#environment keys an environment on its id and not
/// on the name it displays, because a name can be shared by two machines and
/// changed on one.
/// </para>
/// <para>
/// <paramref name="ServerTimestamp"/> is the store's stamp rather than a clock
/// any device controls, so two machines with skewed clocks still agree on the
/// order the service saw. Session records are single-writer and have no
/// last-write-wins tie to break (.arc42/adr/0005 §Session records); the stamp
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
