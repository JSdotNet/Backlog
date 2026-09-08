using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.Sessions;

/// <summary>
/// The sanitization boundary, as one pure function each way.
/// <para>
/// <strong>This is the whole reason a session record may leave the machine, and
/// it is deliberately the only place a record is built.</strong>
/// .arc42/adr/0005 §Session records states a whitelist of ten fields and says in
/// as many words that a whitelist and a filter fail in opposite directions: a
/// filter that misses a field leaks it, a whitelist that misses one merely omits
/// it. <see cref="SessionRecord"/> makes that structural — a field not in the
/// table does not exist on the type. This class is the second half of the same
/// property: one function, no state, no clock, no I/O, so "what leaves this
/// machine" is a question with exactly one place to read the answer.
/// </para>
/// <para>
/// <strong><see cref="AgentSession.WorkingFolder"/> and
/// <see cref="AgentSession.Title"/> never leave.</strong> The working folder is a
/// raw absolute path — it describes one machine's disk, means nothing on the
/// machine that read it, and .arc42/adr/0005 §Scope lists paths among the four
/// things that stay out precisely because the receiving machine would then act on
/// one. The title is worse: an agent derives it from what the person typed, so it
/// is a fragment of a prompt, and prompts are the first thing named under "never
/// prompts, never tool output, never file contents". Neither is on the wire type
/// at all, and <c>SessionRecordMappingTests</c> asserts over the serialized body
/// rather than over the record, because a test on the record would not notice
/// somebody widening the wire.
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
    public static SessionRecord ToRecord(AgentSession session, ISessionRepositoryAliases aliases)
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
            DurationSecondsOf(session.StartedAt, session.LastActivityAt));
    }

    /// <summary>
    /// One record from another environment, as this device's read model holds it.
    /// </summary>
    /// <param name="entry">What came back from the feed, machine id and all.</param>
    /// <param name="now">The clock the state is derived against. Passed in rather
    /// than read, which is what makes the staleness boundary testable at all.</param>
    public static AgentSession ToSession(SessionRecordEntry entry, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var record = entry.Record;

        return new AgentSession(
            record.SessionId,
            KindFor(record.AgentKind),
            entry.MachineId.ToString(),
            record.MachineName,
            // Titles do not sync, so there is none to show and none to invent. The
            // session id is what the record actually carries that names this
            // session to a person, and it reads as an identifier rather than as a
            // sentence somebody wrote — which is the point. Anything prettier would
            // be this device composing a description of work it never saw.
            record.SessionId,
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
            AgentSessionOrigin.Replicated);
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
    /// agent recorded no repository, and it means only that.
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
