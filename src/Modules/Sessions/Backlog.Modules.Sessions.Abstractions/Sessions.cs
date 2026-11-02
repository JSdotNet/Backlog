namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// Which assistant a session belongs to.
/// <para>
/// Dev PC Management's model called these Copilot sessions, because Copilot was the
/// only agent the machines ran when it was written. Two agents run on them now, and
/// the honest widening is a type on the session rather than a list per vendor —
/// which would make "what is running here" a question nobody could answer without
/// first knowing how many vendors there are. Widening the word that far is also part
/// of why sessions became their own context: an agent is not a Copilot, and a model
/// that says so does not belong to the context named after the PC.
/// </para>
/// </summary>
public enum AgentSessionKind
{
    Claude,
    Copilot
}

/// <summary>
/// How far along a session is.
/// <para>
/// Three members, not five. The control library's <c>IntegrationSessionState</c>
/// also carries Starting, Waiting and Failed, and none of those is derivable from
/// what a session leaves on disk: a transcript that stops says nothing about
/// whether it was answered, abandoned or crashed. Claiming Failed from an absent
/// file would be inventing a fact, so this enum stops at what the files support
/// and the pane maps these three onto the library's vocabulary.
/// </para>
/// </summary>
public enum AgentSessionState
{
    /// <summary>The agent is on the machine now and something moved recently.</summary>
    Running,

    /// <summary>Still registered as live, but nothing has moved for longer than
    /// <see cref="AgentSessionStates.StaleAfter"/>. A left-open window, usually.</summary>
    Stalled,

    /// <summary>Over. Only its record is left.</summary>
    Finished,

    /// <summary>Live, and holding an open waiting question: it asked the person something
    /// and no answer has reached it yet (local ADR 0041). Read in from the question relay
    /// by <see cref="WaitingQuestions.StateWith"/>, never from a file; last, so the
    /// members before it keep their values.</summary>
    Waiting
}

/// <summary>
/// When a live session stops counting as live.
/// <para>
/// The threshold sits here rather than in the control library on that library's
/// own instruction: it says in as many words that Stalled is "computed by the
/// host, never here", because a threshold is a policy and a policy needs a clock.
/// This is the host side of that sentence.
/// </para>
/// </summary>
public static class AgentSessionStates
{
    /// <summary>Half an hour of silence. Long enough that a reader thinking about
    /// a change is not called stalled, short enough that yesterday's forgotten
    /// terminal does not sit in the list claiming to be running.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    /// <summary>Running, or Stalled once the silence has gone on too long. The
    /// clock is passed in rather than read, which is what makes the boundary
    /// testable at all.</summary>
    public static AgentSessionState Of(DateTimeOffset lastActivity, DateTimeOffset now) =>
        now - lastActivity > StaleAfter ? AgentSessionState.Stalled : AgentSessionState.Running;
}

/// <summary>
/// How this device came by a session record.
/// <para>
/// Not a fact about the session. A session does not know whether the machine
/// reading it is the one that ran it, and nothing an agent writes on disk says so
/// — which is why this is not on ADR 0005's sync whitelist and never travels: the
/// pushing code leaves it out, and the source that receives replicated records
/// stamps <see cref="Replicated"/> on everything it produces. Two devices holding
/// the same session therefore disagree about this one field, on purpose, and that
/// disagreement is the entire content of it.
/// </para>
/// <para>
/// A field rather than something a surface derives from
/// <c>EnvironmentId != thisDevice</c>, and that is the part worth arguing. The
/// source already knows without comparing anything — it either read this machine's
/// own profile or was handed a record from elsewhere — and a comparison cannot get
/// there, because the two are not the same question: a record this device pushed
/// and later received back names this environment while having arrived over the
/// wire, and derivation would call it local. Deriving it would also thread the
/// device identity into every surface that shows a session, and a second surface
/// deriving it separately is a second definition that only has to drift once to
/// have two rows disagree about the same session.
/// </para>
/// </summary>
public enum AgentSessionOrigin
{
    /// <summary>Read from this machine's own profile, off the agent's own files.
    /// Re-readable: whatever it says can be checked again by looking.</summary>
    Local,

    /// <summary>Reported by another environment. As true as what that environment
    /// sent and as current as the last sync, which is not the standing a file this
    /// device can open for itself has.</summary>
    Replicated,

    /// <summary>This machine's own session, answered from its session record because
    /// the agent's files no longer say anything about it — cleaned away, most often.
    /// As true as the last reading that amended the record, and never re-readable.
    /// See <see cref="IAgentSessionRecordStore"/>.</summary>
    Recorded
}

/// <summary>
/// One assistant session, as far as the machine it ran on can describe it.
/// <para>
/// <b>Duration is deliberately not a field here.</b> It is exactly
/// <c>LastActivityAt - StartedAt</c>, both of which are on the record already, and a
/// stored third value can disagree with its own two operands — a row claiming forty
/// minutes between two timestamps ten minutes apart is wrong in a way that renders
/// perfectly and that no reader can catch. It is derived in one place instead
/// (<c>src/Modules/Dashboard/Backlog.Modules.Dashboard/Services/SessionInsights.cs</c>),
/// which is also the only place that has to decide what an open-ended session's
/// duration means. ADR 0005's sync whitelist does name a duration count, and the
/// wire contract carries one, because the relaying service is deliberately dumb
/// about the fields it forwards and cannot subtract two of them; that is the wire's
/// business rather than this model's. The omission here is the decision, not an
/// oversight to be tidied up later.
/// </para>
/// </summary>
/// <param name="Id">The agent's own identifier for the session.</param>
/// <param name="Kind">Which assistant.</param>
/// <param name="EnvironmentId">
/// The stable identifier of the environment this session ran in — ADR 0005's machine
/// id, on the session record.
/// <para>
/// Beside <see cref="Environment"/> rather than instead of it, because they answer
/// two different questions: this one says which environment, and that one says what
/// it is called. A name cannot do both — a machine can be renamed and two machines
/// can share a name — so anything that groups, filters or reconciles sessions keys
/// on this and shows that.
/// </para>
/// <para>
/// A string rather than a <c>Guid</c>, for the reason <see cref="Id"/> and
/// <see cref="Repository"/> are strings: this contract holds identifiers exactly as
/// the source stated them. The local reader is handed the device identity's Guid
/// formatted out, and an environment that is not a device — a container, a hosted
/// runner — can answer with whatever identifier it has without this record deciding
/// what shape that has to be.
/// </para>
/// </param>
/// <param name="Environment">
/// Where the session ran. This context's own term, and deliberately not the same
/// word as Dev PC Management's Machine: an environment is wherever an agent can run
/// — a development PC today, and there is nothing in this model that stops it being
/// a container or a hosted runner tomorrow. Where the environment is this device, the
/// identity behind <see cref="EnvironmentId"/> is the kernel's device identity, which
/// is what makes a session and a registered Machine the same thing rather than two
/// things that happen to agree. What is looked up from Dev PC Management is the
/// display name and whatever else a registered machine knows — a
/// <c>Customer/Supplier</c> relationship over the name, not over the identity; see
/// <c>.devbook/domain/sessions/dependencies.md</c>.
/// <para>
/// Every session discovered locally carries the current machine name, because
/// neither agent records a hostname in what it writes; a session from another
/// environment can only arrive once that environment reports it.
/// </para>
/// </param>
/// <param name="Title">What to call it in a list.</param>
/// <param name="WorkingFolder">Where the agent was working.</param>
/// <param name="Repository">
/// <c>owner/name</c> where the agent recorded it, and null where it did not. Null
/// rather than derived from the folder: a path leaf is a guess, and a wrong
/// repository attributed to a session is worse than an em dash.
/// </param>
/// <param name="Branch">The branch the agent recorded, where it recorded one.</param>
/// <param name="StartedAt">Null where the agent left nothing to date the start from.</param>
/// <param name="LastActivityAt">Always known — at worst the file's own timestamp.</param>
/// <param name="State">See <see cref="AgentSessionState"/>.</param>
/// <param name="TurnCount">
/// How many turns the person took in this session, where the agent recorded enough
/// to count them, and null where it did not.
/// <para>
/// <b>A turn is one exchange the person initiated: a prompt they sent.</b> The word
/// is ambiguous enough that two readers left to themselves would count two different
/// things and the number would mean neither, so it is defined here and every reader
/// either counts what this sentence says or reports null. Not a message — an
/// assistant's replies are messages too, and a transcript holds far more of those
/// than anything else. Not a tool call, of which one prompt can produce dozens: on
/// this product's own transcripts the two differ by more than thirty times, so
/// counting the wrong one is not a rounding error but a different number entirely.
/// </para>
/// <para>
/// Nullable, and never <c>0</c> standing in for absent. The Session Log's invariant
/// is that the log never fills a gap the agent left
/// (<c>.devbook/domain/sessions/domain.md#session-log</c>), and 0 is a count: it says a
/// person opened a session and never spoke in it, which is a claim about what
/// happened. Null says the agent left nothing to count from — a Copilot session, a
/// transcript that could not be read — which is the only thing a reader in that
/// position actually knows. Anything that aggregates these skips nulls rather than
/// adding them as zero, for the same reason.
/// </para>
/// <para>
/// On ADR 0005's sync whitelist as <em>Turn count</em>, which is why the model
/// carries it at all: a record that arrives from another environment has no
/// transcript behind it that this device could count for itself.
/// </para>
/// </param>
/// <param name="Origin">
/// How this device came by the record. See <see cref="AgentSessionOrigin"/>; it is
/// stamped by the source that produced the record and is the one field on here that
/// does not describe the session.
/// </param>
public sealed record AgentSession(
    string Id,
    AgentSessionKind Kind,
    string EnvironmentId,
    string Environment,
    string Title,
    string WorkingFolder,
    string? Repository,
    string? Branch,
    DateTimeOffset? StartedAt,
    DateTimeOffset LastActivityAt,
    AgentSessionState State,
    int? TurnCount,
    AgentSessionOrigin Origin)
{
    /// <summary>
    /// The repository this product places the session in — <c>owner/name</c>, from
    /// the working folder lying inside a registered repository's clone — or null
    /// where no registered clone contains it.
    /// <para>
    /// <b>A second field, never written into <see cref="Repository"/>.</b> That one
    /// is what the agent said; this one is what this product worked out, and the
    /// two must stay tellable apart because they fail differently. A recorded
    /// repository is a fact about the session. A resolved one is a fact about this
    /// machine's Repositories screen — that somebody registered a clone at a folder
    /// the session happened to be under — and it is exactly as good as that
    /// registration. Folding it into the recorded field would have every surface
    /// treat the two with one confidence, which is the outcome
    /// <c>.devbook/domain/sessions/domain.md#working-location</c> forbids when it says a
    /// guessed repository is indistinguishable from a recorded one.
    /// </para>
    /// <para>
    /// Not a guess in that sense, which is why it exists at all. The rule there is
    /// against reading a repository off a path leaf: <c>D:\Repos\Backlog</c> does
    /// not say which of GitHub's several <c>Backlog</c>s it is a clone of. This is
    /// a lookup against a fact the product already holds — a clone directory the
    /// person registered against an <c>owner/name</c> — and the folder is either
    /// under it or not. Claude records no repository in anything it writes, so
    /// without this a header scoped to one repository hides every Claude session
    /// on the machine, running ones included; see <see cref="ISessionRepositoryResolver"/>.
    /// </para>
    /// <para>
    /// Resolved where the session is read, because only the machine that ran the
    /// session has both the folder and the clone it lies under, and carried on the
    /// wire from there: a replicated record has no folder to resolve from and holds
    /// whatever its origin resolved. An <c>init</c> property rather than a
    /// positional parameter, so the readers construct what they read and the source
    /// stamps this afterwards — the same shape as <see cref="Origin"/>, a fact
    /// about how this device sees the session rather than one the agent wrote.
    /// </para>
    /// </summary>
    public string? ResolvedRepository { get; init; }

    /// <summary>
    /// The delivery dashboards' key for the working folder, carried for a session
    /// whose folder did not come with it — a replicated record — or null.
    /// <para>
    /// A local session leaves this null and is matched from its folder, which can
    /// try every folder above it (<see cref="DeliveryRunWorktrees.KeysUpFrom"/>). A
    /// record carries the key of the folder alone, because the keys above it would
    /// send every parent folder's name along; a session started below its worktree's
    /// top level therefore matches its runs on the machine that ran it and not from
    /// the record.
    /// </para>
    /// </summary>
    public string? WorktreeKey { get; init; }

    /// <summary>Where the agent was run from — <c>claude-desktop</c>, <c>cli</c>, … —
    /// where it recorded that, or null.</summary>
    public string? Entrypoint { get; init; }

    /// <summary>The pull requests the session linked itself to, or null where the
    /// reader cannot say: Copilot records none, and an unreadable transcript says
    /// nothing. Empty is a reading that found none.</summary>
    public IReadOnlyList<AgentPullRequest>? PullRequests { get; init; }

    /// <summary>What the session spent per model, on the same null-and-empty terms as
    /// <see cref="PullRequests"/>.</summary>
    public IReadOnlyList<AgentModelUsage>? ModelUsage { get; init; }

    /// <summary>
    /// Whether the Claude desktop app has archived this session: <c>false</c> where its
    /// session store holds a record that says the session is still in its sidebar,
    /// <c>true</c> where the record says it was archived, and null where there is no
    /// record to ask.
    /// <para>
    /// <b>Null is not false.</b> A CLI-only Claude session, every Copilot one and
    /// every record that arrived from somewhere else has no desktop record behind it,
    /// and unknown never reads as open: <see cref="AgentSessionView.Open"/> keeps only
    /// <c>false</c>. A record that leaves the flag out is null for the same reason —
    /// the store is the desktop app's own and undocumented, and an absent property
    /// states nothing.
    /// </para>
    /// <para>
    /// A fact read off this machine's disk, never derived from <see cref="State"/>: an
    /// open session with no process running reads Finished, as every session with no
    /// liveness evidence does. And it never travels, like <see cref="Origin"/>: it is
    /// not on ADR 0005's sync whitelist, the pushing code leaves it out, and a session
    /// record keeps no copy of it. An <c>init</c> property, stamped by the reader that
    /// found the desktop record.
    /// </para>
    /// </summary>
    public bool? Archived { get; init; }

    /// <summary>
    /// The Claude desktop app's id for the scheduled task this session is a run of, or
    /// null where the session's desktop record names none or there is no record.
    /// <para>
    /// The desktop app's routine schedule, and not <see cref="SessionRow.Schedule"/>,
    /// which is a delivery run's. Local-only on the same terms as <see cref="Archived"/>.
    /// </para>
    /// </summary>
    public string? ScheduledTask { get; init; }

    /// <summary>
    /// The name the Claude desktop app's sidebar shows for this session — the record's
    /// <c>title</c>, whether the app, a tool or the person gave it — or null where the
    /// record names none or there is no record.
    /// <para>
    /// What a surface calls the session where it is set, ahead of <see cref="Title"/>,
    /// which is the transcript's and often only the worktree folder's name. It never
    /// replaces <see cref="Title"/>: that is what a session record keeps and sync
    /// carries, and this is local-only on the same terms as <see cref="Archived"/>.
    /// </para>
    /// </summary>
    public string? DesktopTitle { get; init; }

    /// <summary>
    /// The Claude desktop app's own id for the session — <c>local_&lt;guid&gt;</c>, the id its
    /// session tools take — where its session store holds a record, else null. What the
    /// Manager sends an answer delivery to (local ADR 0041 §3); local-only on the same
    /// terms as <see cref="Archived"/>.
    /// </summary>
    public string? DesktopSessionId { get; init; }
}

/// <summary>
/// How many sessions a source will describe per agent.
/// <para>
/// A cap on work and on the length of a list, not a claim about what exists — which
/// is why <see cref="AgentSessionCatalog.Discovered"/> travels beside the sessions
/// and the surface says what it dropped. A developer's profile holds hundreds of
/// records: this machine's held 842, and 705 of them were Copilot's, so an uncapped
/// list was 40,000 pixels of table in which the four running sessions were four
/// rows in eight hundred.
/// </para>
/// <para>
/// Per agent rather than one cap over the merged list, and that is the part worth
/// arguing. A single cap of 200 over those 842 would have been filled almost
/// entirely by whichever agent happened to have written most recently — on this
/// machine, Copilot — and Claude's sessions would have been squeezed out of a
/// surface whose whole point is showing both. An even split cannot do that to
/// either of them.
/// </para>
/// </summary>
public static class AgentSessionLimits
{
    /// <summary>The most recent this many sessions from each agent.</summary>
    public const int PerAgent = 100;

    /// <summary>
    /// How far back a reading <see cref="AgentSessionQuery.Since"/> a horizon is
    /// promised to reach on every source, whatever the per-agent cap.
    /// <para>
    /// The cap is the right shape for a list and the wrong one for a count. A surface
    /// that asks "how many sessions ran in the last twenty-six weeks" and is answered with
    /// the newest hundred per agent reads a page size back as a total — 200 on every
    /// machine, which is what the Dashboard showed. So a reading can be asked for
    /// everything since a horizon instead, and this is the horizon a source that keeps
    /// somebody else's records has to keep them for: a store that retained less would
    /// answer the same question short for every other machine.
    /// </para>
    /// <para>
    /// Twenty-six weeks because that is the longest period the Dashboard offers. The
    /// Dashboard cannot name this constant — its module may not see this one — so the
    /// two are paired by this sentence and by <c>SessionInsights.Horizon</c>'s.
    /// </para>
    /// </summary>
    public static readonly TimeSpan History = TimeSpan.FromDays(7 * 26);
}

/// <summary>
/// How much of an environment's history one reading describes.
/// <para>
/// Two shapes and no third. <see cref="Newest"/> is the inventory's: the most recent
/// <see cref="AgentSessionLimits.PerAgent"/> from each agent, which is a list a person
/// can scroll and a read a profile of hundreds can afford on every refresh.
/// <see cref="Since"/> is the count's: every session whose last activity is at or after
/// a horizon, with no cap at all, because a figure derived from a capped list is a
/// floor pretending to be a total. A reading answered <c>Since</c> is never
/// <see cref="AgentSessionCatalog.Capped"/> by the per-agent limit — only by a source
/// that does not hold records as far back as it was asked.
/// </para>
/// <para>
/// A record with a factory per shape rather than a nullable parameter on the port, so a
/// caller reads what it asked for and a source cannot mistake "no horizon" for "since
/// forever".
/// </para>
/// <para>
/// Either shape can also be asked for at a <see cref="Detail"/>: in full, which is what
/// both factories answer, or <see cref="TimestampsOnly"/> — the same sessions, with only
/// what a source knows without opening a transcript. That is a depth and not a third
/// shape, because it changes which fields a session carries and never which sessions
/// are in the list: a source that ignores it returns more than was asked for, which is
/// still the right answer. See <see cref="AgentSessionDetail.Timestamps"/> for what such
/// a reading promises, and for who must never ask for one.
/// </para>
/// </summary>
public sealed record AgentSessionQuery
{
    private AgentSessionQuery(DateTimeOffset? horizon) => Horizon = horizon;

    /// <summary>The most recent <see cref="AgentSessionLimits.PerAgent"/> sessions from
    /// each agent, and how many there were before the cap.</summary>
    public static AgentSessionQuery Newest { get; } = new(horizon: null);

    /// <summary>Every session whose last activity is at or after
    /// <paramref name="horizon"/>, uncapped.</summary>
    public static AgentSessionQuery Since(DateTimeOffset horizon) => new(horizon);

    /// <summary>The horizon, or null for the newest-per-agent shape.</summary>
    public DateTimeOffset? Horizon { get; }

    /// <summary>Whether this is the newest-per-agent shape. The other is
    /// <see cref="Horizon"/> being set.</summary>
    public bool IsNewest => Horizon is null;

    /// <summary>How much of each session the reading carries: <see cref="AgentSessionDetail.Full"/>
    /// unless <see cref="TimestampsOnly"/> asked for less.</summary>
    public AgentSessionDetail Detail { get; private init; } = AgentSessionDetail.Full;

    /// <summary>The same shape, at the <see cref="AgentSessionDetail.Timestamps"/>
    /// depth.</summary>
    public AgentSessionQuery TimestampsOnly() => this with { Detail = AgentSessionDetail.Timestamps };
}

/// <summary>How much of each session a reading carries.</summary>
public enum AgentSessionDetail
{
    /// <summary>Everything a source can say about a session, transcripts read.</summary>
    Full,

    /// <summary>
    /// What a source knows without opening a transcript: the session's
    /// <see cref="AgentSession.Id"/>, <see cref="AgentSession.Kind"/>,
    /// <see cref="AgentSession.State"/>, <see cref="AgentSession.StartedAt"/> and
    /// <see cref="AgentSession.LastActivityAt"/>, the same as a full reading gives them.
    /// <para>
    /// For a consumer that asks when and in what state — a task's linked-session
    /// badge, a roadmap entry dated by its sessions — and would otherwise have every
    /// transcript in the reading parsed to throw the result away. What only a
    /// transcript states is absent rather than guessed: no branch, turn count, entry
    /// point or work, and a past session's folder empty and its title the short id. A
    /// source that has those fields without opening anything may still answer them.
    /// </para>
    /// <para>
    /// <strong>Never the reading a record is built from.</strong> The session record
    /// keeper and the push both build records from what they read, and a thin reading
    /// would carry its absences to this machine's log and to every other machine's.
    /// </para>
    /// </summary>
    Timestamps
}

/// <summary>
/// What a source found, what it could not read, and how much there was.
/// <para>
/// Two lists rather than a throw. Sessions come from two independent places on
/// disk, and one of them being unreadable — a folder that does not exist because
/// that agent was never installed, a permission error — is not a reason to show
/// the reader nothing. The unreadable sources are named so the surface can say
/// which half of the picture is missing instead of quietly presenting the other
/// half as the whole.
/// </para>
/// </summary>
/// <param name="Sessions">What the source will describe: up to
/// <see cref="AgentSessionLimits.PerAgent"/> from each agent for
/// <see cref="AgentSessionQuery.Newest"/>, everything at or after the horizon for
/// <see cref="AgentSessionQuery.Since"/>.</param>
/// <param name="Unreadable">The sources that could not be read, by name.</param>
/// <param name="Discovered">
/// How many sessions existed that the query asked about, before any cap — the same as
/// the list's length for a horizon reading a source could reach. Sessions and not files: an agent can
/// file one session twice — a live marker beside its own transcript, or a transcript
/// under two project folders after the session's cwd changed — and both halves of
/// this number collapse those before counting, so it does not mean one thing for
/// live sessions and another for past ones. Carried so a capped list can say so: a
/// surface that showed 200 of 842 without mentioning the 842 would be presenting a
/// truncated list as the whole history, which is the one thing a capped list must
/// not do.
/// </param>
public sealed record AgentSessionCatalog(
    IReadOnlyList<AgentSession> Sessions,
    IReadOnlyList<string> Unreadable,
    int Discovered)
{
    public static AgentSessionCatalog Empty { get; } = new([], [], 0);

    /// <summary>Whether the cap took anything. Not a comparison a caller should have
    /// to remember the direction of.</summary>
    public bool Capped => Discovered > Sessions.Count;

    /// <summary>
    /// How many of <see cref="Sessions"/> are open in the Claude desktop app and older
    /// than a session the cap dropped — listed only because an open session is never
    /// dropped by the cap (<see cref="AgentSessionViews.IsOpen"/>). Zero for a horizon
    /// reading, which has no cap, and for every source with no desktop record.
    /// <para>
    /// Carried so a capped list can still say what it holds: without it, "the 132 most
    /// recent" would count months-old open sessions among the most recent, when the
    /// list is the most recent hundred plus the older ones still open. The rest of
    /// <see cref="Sessions"/> — <c>Sessions.Count - OpenBeyondCap</c> — are exactly the
    /// most recent ones: nothing newer than any of them was left out.
    /// </para>
    /// </summary>
    public int OpenBeyondCap { get; init; }
}

/// <summary>
/// Where the sessions on a machine come from. The port; whether it reads local
/// files, asks a registered machine, or answers from a fixture is the host's
/// business.
/// </summary>
public interface IAgentSessionSource
{
    /// <summary>The inventory's reading: <see cref="AgentSessionQuery.Newest"/>.</summary>
    Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>The reading <paramref name="query"/> names. Two members rather than a
    /// defaulted parameter so an implementation has to say what it does with a horizon
    /// instead of quietly answering the capped list to a question about a window.
    /// <para>
    /// The query's <see cref="AgentSessionQuery.Detail"/> is a property and not a third
    /// member, and that is not the same argument lost: a horizon decides which sessions
    /// are answered, and getting it wrong answers the wrong set. A depth only decides
    /// which fields are filled, so an implementation that ignores it answers more than
    /// was asked for, which is still right.
    /// </para></summary>
    Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default);
}

/// <summary>Which sessions the reader wants in front of them at all.</summary>
public enum AgentSessionView
{
    /// <summary>
    /// The ones the reading called Running or Stalled.
    /// <para>
    /// What that rests on is not the same for both agents, and the difference decides
    /// which sessions this view can hold. Claude writes a file per running session, so
    /// its live rows are evidence. Copilot leaves no liveness marker at all, so its
    /// reader has only a timeout: a session goes Finished once its folder has been
    /// quiet for longer than <see cref="AgentSessionStates.StaleAfter"/>, and it never
    /// reads Stalled. A Copilot session that is genuinely running but quiet therefore
    /// falls out of this view rather than sitting in it under the wrong word.
    /// </para>
    /// </summary>
    Live,

    /// <summary>
    /// The ones the Claude desktop app has not archived — its sidebar, whether or not a
    /// process is running for each.
    /// <para>
    /// A question about the desktop app's record and not about liveness, which is why
    /// it sits beside <see cref="Live"/> rather than widening it. The desktop app ends
    /// an idle session's process, so a session open in its sidebar reads Finished and
    /// stays Finished here: this view admits on <see cref="AgentSession.Archived"/>
    /// being <c>false</c> and never on <see cref="AgentSession.State"/>. A session with
    /// no desktop record is never open, however live it is.
    /// </para>
    /// </summary>
    Open,

    /// <summary>Everything the source described, evidence or not.</summary>
    All
}

/// <summary>
/// Choosing which rows to show. A pure function over the rows it is given, the same
/// way <see cref="AgentSessionGroups"/> is — and the composition is view first, then
/// grouping: a surface filters the list and groups what survived, never the other way
/// round, so a machine with nothing live on it loses its section rather than keeping
/// an empty one.
/// <para>
/// Over <see cref="SessionRow"/>s rather than sessions since the list gained rows a
/// run alone accounts for: a view that only knew sessions would have had to be
/// applied twice, once to each half of a list that is meant to be one.
/// </para>
/// <para>
/// A separate operation rather than a fourth member of
/// <see cref="AgentSessionGrouping"/>, and that is the part worth arguing. A
/// <c>Live</c> grouping would sit in the same strip as Environment and Type while
/// doing something neither of them does — every grouping carries every session, and
/// that is a documented invariant with a test holding it up. Adding a member that
/// dropped rows would falsify it, and would leave the reader with one control whose
/// options sometimes rearrange the list and sometimes shorten it.
/// </para>
/// </summary>
public static class AgentSessionViews
{
    /// <summary>
    /// The rows this view admits, in the order they were given. Ordering is the
    /// grouping's job; a filter that also sorted would be a second answer to what
    /// "most recently active first" means.
    /// </summary>
    public static IReadOnlyList<SessionRow> Of(IReadOnlyList<SessionRow> rows, AgentSessionView view)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return view switch
        {
            AgentSessionView.Live => [.. rows.Where(row => IsLive(row.State))],
            AgentSessionView.Open => [.. rows.Where(row => row.Session is { } session && IsOpen(session))],

            // The same list back, not a copy of it: All is the absence of a filter,
            // and rebuilding the list would be work done to change nothing.
            _ => rows
        };
    }

    /// <summary>
    /// Whether the reading called this session still going.
    /// <para>
    /// Public deliberately, and the only place this product spells the Session Log's
    /// invariant out in code: Running and Stalled both require liveness evidence,
    /// and with none a session is Finished — see <c>.devbook/domain/sessions/domain.md</c>.
    /// A second surface asking "is this one still going" asks here rather than
    /// writing the same two-state test again and drifting from it.
    /// </para>
    /// <para>
    /// The invariant is the domain's; how well a reader can honour it is the
    /// reader's. This asks the state it was handed and nothing more, so it is only
    /// as true as the derivation behind it — which for Copilot is a timeout rather
    /// than evidence. <see cref="AgentSessionView.Live"/> carries that difference in
    /// full; do not read this method's name as a promise the readers all keep.
    /// </para>
    /// </summary>
    public static bool IsLive(AgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return IsLive(session.State);
    }

    /// <summary>The same test over a state alone, which is what a row that may have
    /// no session behind it can offer.</summary>
    public static bool IsLive(AgentSessionState state) =>
        state is AgentSessionState.Running or AgentSessionState.Stalled or AgentSessionState.Waiting;

    /// <summary>
    /// Whether the Claude desktop app holds this session open: its desktop record says
    /// it is not archived. Null — no record — is not open, so a surface asking here
    /// never reads an unknown as an answer.
    /// </summary>
    public static bool IsOpen(AgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.Archived is false;
    }

    /// <summary>What a view is called on screen. Here rather than in the pane, for
    /// the reason <see cref="AgentSessionGroups.Label(AgentSessionKind)"/> is: a
    /// control and a sentence about it cannot disagree if there is one word.</summary>
    public static string Label(AgentSessionView view) => view switch
    {
        AgentSessionView.Live => "Live",
        AgentSessionView.Open => "Open",
        _ => "All"
    };
}

/// <summary>
/// One environment a reading holds a record of, as a filter can offer it.
/// </summary>
/// <param name="Id">The environment's stable identifier — what a narrowing keys on.
/// Not the name: a machine can be renamed and two can share a name, so a filter
/// keyed on the name would quietly merge or split what it is filtering.</param>
/// <param name="Name">What a person recognises in a list: the name the environment's
/// most recent session carries, so a renamed machine is offered under the name it
/// has now.</param>
public sealed record AgentSessionEnvironment(string Id, string Name);

/// <summary>
/// Narrowing a set of rows to one environment, and saying which environments there
/// are to narrow to. A pure function over the rows it is given, like
/// <see cref="AgentSessionViews"/> beside it, and composed the same way: narrow
/// first, then group, so an environment left out never keeps an empty section.
/// <para>
/// Derived from the sessions rather than asked of a directory, and that is the part
/// worth arguing. An environment is offered exactly when there are records behind it
/// to narrow to; a filter option that can only ever empty the list is not something
/// this can produce. It also means the filter's options and the grouping's sections
/// cannot disagree — same ids, same names, same order — because they are read off
/// the same grouping.
/// </para>
/// <para>
/// Separate from <see cref="AgentSessionViews"/> rather than a third member of it.
/// A view is a question about liveness with two answers; which machine is a
/// question about place, with as many answers as machines have reported, and
/// folding the two into one enum would make the pane's "Live" mean "live here" on
/// some presses and "live anywhere" on others. Two narrowings with one guarantee
/// each — both remove rows and neither reorders — and the surface reports what the
/// two together left out.
/// </para>
/// </summary>
public static class AgentSessionEnvironments
{
    /// <summary>
    /// Every environment these sessions name, in the order a grouping by environment
    /// draws its sections: by name, then by id. Read off <see cref="AgentSessionGroups"/>
    /// deliberately, so a filter option and the section heading it corresponds to are
    /// one derivation rather than two that only have to drift once.
    /// </summary>
    public static IReadOnlyList<AgentSessionEnvironment> Of(IReadOnlyList<SessionRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return
        [
            .. AgentSessionGroups.Of(rows, AgentSessionGrouping.Environment)
                .Select(group => new AgentSessionEnvironment(
                    group.Rows[0].EnvironmentId,
                    // The heading is the name the newest row carries, and it can be
                    // blank the way any wire field can. An option with an empty label
                    // is one a reader cannot tell from "All machines" above it, so
                    // the id stands in: it at least identifies what it narrows to.
                    string.IsNullOrWhiteSpace(group.Name) ? group.Rows[0].EnvironmentId : group.Name))
        ];
    }

    /// <summary>
    /// The rows on one environment, in the order they were given, or every row when
    /// no environment is named. Keyed on the id, ordinally, because that is how
    /// <see cref="AgentSessionGroups"/> decides which section a row is in.
    /// </summary>
    public static IReadOnlyList<SessionRow> On(IReadOnlyList<SessionRow> rows, string? environmentId)
    {
        ArgumentNullException.ThrowIfNull(rows);

        // The same list back, not a copy of it: no environment is the absence of a
        // filter, the same way AgentSessionView.All is.
        if (string.IsNullOrWhiteSpace(environmentId)) return rows;

        return [.. rows.Where(row => string.Equals(row.EnvironmentId, environmentId, StringComparison.Ordinal))];
    }
}

/// <summary>How the reader wants the list carved up.</summary>
public enum AgentSessionGrouping
{
    /// <summary>One flat list, most recently active first.</summary>
    None,

    /// <summary>A section per machine.</summary>
    Environment,

    /// <summary>A section per assistant.</summary>
    Kind,

    /// <summary>The live sessions first, then the finished ones by the day they were
    /// last active on the reader's clock: earlier today, yesterday, earlier this
    /// week, older.</summary>
    When,

    /// <summary>A section per repository, by the one the row shows.</summary>
    Repository,

    /// <summary>A section per plan tag (<see cref="SessionTitles"/>): the plan's
    /// session, then its runs, then its entries by step; the rows of no plan last.</summary>
    Plan,

    /// <summary>A section per <see cref="SessionPurpose"/>, in the order it declares.</summary>
    Purpose
}

/// <summary>
/// One section of a grouped list. <c>Name</c> is null for the ungrouped case, so a
/// caller renders sections and never has to branch on the grouping again.
/// <para>
/// <c>Key</c> is what made the section one section, and it is carried because the
/// name does not always say: the environment id under Environment grouping, the
/// kind's name under Kind grouping, null when nothing grouped. Two machines that
/// share a name are two sections here — <see cref="AgentSessionGroups.Of"/> keys on
/// the id for exactly that reason — and a renderer keying its sections on the
/// heading alone would collapse them back into one, or refuse to render at all.
/// The key travels so the renderer can key on what the grouping keyed on.
/// </para>
/// </summary>
public sealed record AgentSessionGroup(string? Name, IReadOnlyList<SessionRow> Rows, string? Key = null);

/// <summary>
/// Carving the list up. A pure function over the rows it is given: no I/O, no
/// clock, no state — which is what lets the grouping be tested without a
/// filesystem underneath it, and what keeps the pane from growing a second
/// definition of "by type".
/// </summary>
public static class AgentSessionGroups
{
    /// <summary>
    /// Sections in a stable order, each ordered most recently active first.
    /// <para>
    /// Group order is deliberately not by size. A list that reorders its own
    /// sections as sessions come and go makes the reader re-find the section they
    /// were reading, so machines sort by name and assistants sort in the order
    /// <see cref="AgentSessionKind"/> declares them.
    /// </para>
    /// </summary>
    public static IReadOnlyList<AgentSessionGroup> Of(
        IReadOnlyList<SessionRow> rows,
        AgentSessionGrouping grouping) =>
        Of(rows, grouping, DateTimeOffset.UtcNow, TimeZoneInfo.Local);

    /// <summary>
    /// The same sections, against a clock and a time zone — the two things
    /// <see cref="AgentSessionGrouping.When"/> needs and no other grouping reads.
    /// <para>
    /// The day a finished session lands in is the reader's day: "yesterday" means
    /// the day before the one on the reader's own clock, not UTC's. The clock is the
    /// moment the list was read rather than the moment it is drawn, so a press in the
    /// filter bar never moves a row from one day into another.
    /// </para>
    /// </summary>
    public static IReadOnlyList<AgentSessionGroup> Of(
        IReadOnlyList<SessionRow> rows,
        AgentSessionGrouping grouping,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(zone);

        var ordered = rows.OrderByDescending(row => row.LastActivityAt).ToList();

        return grouping switch
        {
            // Keyed by the environment's id and named after it, which are two
            // deliberately different things. The id is what makes a section one
            // environment: a name cannot, because a machine can be renamed and two
            // machines can share a name, and both of those turn into a section that is
            // either split or merged for no reason a reader could see. The heading is
            // then the name the most recent session in the section carries, so a
            // renamed machine shows the name it has now rather than the one it had when
            // its oldest session was recorded. Locally the two keys coincide and
            // nothing on screen moves; the difference is the point of having an id at
            // all. Sections still sort by name, as the summary above promises.
            AgentSessionGrouping.Environment =>
            [
                .. ordered
                    .GroupBy(row => row.EnvironmentId, StringComparer.Ordinal)
                    .Select(group => new AgentSessionGroup(group.First().Environment, [.. group], group.Key))
                    .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(group => group.Key, StringComparer.Ordinal)
            ],
            AgentSessionGrouping.Kind =>
            [
                .. ordered
                    .GroupBy(row => row.Kind)
                    .OrderBy(group => group.Key)
                    .Select(group => new AgentSessionGroup(Label(group.Key), [.. group], group.Key.ToString()))
            ],
            AgentSessionGrouping.When => ByWhen(ordered, now, zone),

            // Keyed case-blind, because an owner/name is: JSdotNet/Backlog and
            // jsdotnet/backlog are one repository written twice. Named after the
            // spelling the most recent row carries, as a machine's section is. The
            // rows that name no repository close the list in a section of their
            // own — last, because they are the ones a reader is least likely to be
            // looking for, and kept, because grouping never hides a row.
            AgentSessionGrouping.Repository =>
            [
                .. ordered
                    .Where(row => row.Repository is not null)
                    .GroupBy(row => row.Repository!, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new AgentSessionGroup(group.First().Repository, [.. group], group.First().Repository))
                    .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase),
                .. ordered.Any(row => row.Repository is null)
                    ? [new AgentSessionGroup(NoRepository, [.. ordered.Where(row => row.Repository is null)])]
                    : Array.Empty<AgentSessionGroup>()
            ],
            AgentSessionGrouping.Plan => ByPlan(ordered),

            // In the order SessionPurpose declares, which is the order the filter offers.
            AgentSessionGrouping.Purpose =>
            [
                .. ordered
                    .GroupBy(row => SessionTitles.Of(row).Purpose)
                    .OrderBy(group => group.Key)
                    .Select(group => new AgentSessionGroup(SessionTitles.Label(group.Key), [.. group], group.Key.ToString()))
            ],
            _ => ordered.Count == 0 ? [] : [new AgentSessionGroup(null, ordered)]
        };
    }

    /// <summary>The heading over the rows that name no repository.</summary>
    public const string NoRepository = "No repository";

    /// <summary>The heading over the rows that belong to no plan, under
    /// <see cref="AgentSessionGrouping.Plan"/>.</summary>
    public const string NotPartOfAPlan = "Not part of a plan";

    /// <summary>The heading of the When section that opens the list with the standing
    /// sessions (<see cref="SessionTitles.IsStanding"/>).</summary>
    public const string Standing = "Standing";

    /// <summary>The key of the When section <see cref="Standing"/> heads.</summary>
    public const string StandingKey = "standing";

    /// <summary>
    /// What a Plan section's key starts with, before the bare tag lower-cased. A tag is
    /// free text a title writes, so a bare one could be <c>scheduled</c> and collide with
    /// <see cref="ScheduledKey"/>, the section <see cref="ScheduledApart"/> closes the
    /// list with; under its own prefix it cannot.
    /// </summary>
    public const string PlanKeyPrefix = "plan/";

    /// <summary>The bare tag a Plan section's key names, or null for a key that is
    /// not a Plan section's.</summary>
    public static string? PlanTagOf(string? key) =>
        key is not null && key.StartsWith(PlanKeyPrefix, StringComparison.Ordinal) ? key[PlanKeyPrefix.Length..] : null;

    // Keyed on the bare tag case-blind — an entry writes "foo:1", a run "+foo:execute-plan",
    // and both are one plan. Inside a section the plan's own session leads, its runs
    // follow and its entries close it by step; a stable sort keeps recency within each.
    private static IReadOnlyList<AgentSessionGroup> ByPlan(IReadOnlyList<SessionRow> ordered)
    {
        static int Rank(SessionPurpose purpose) => purpose switch
        {
            SessionPurpose.Plan => 0,
            SessionPurpose.PlanRun => 1,
            _ => 2
        };

        var tagged = ordered.Where(row => SessionTitles.Of(row).PlanTag is not null).ToList();
        var untagged = ordered.Where(row => SessionTitles.Of(row).PlanTag is null).ToList();

        return
        [
            .. tagged
                .GroupBy(row => SessionTitles.Of(row).PlanTag!.ToLowerInvariant(), StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new AgentSessionGroup(
                    "+" + SessionTitles.Of(group.First()).PlanTag,
                    [
                        .. group
                            .OrderBy(row => Rank(SessionTitles.Of(row).Purpose))
                            .ThenBy(row => SessionTitles.Of(row).Step ?? 0)
                    ],
                    PlanKeyPrefix + group.Key)),
            .. untagged.Count > 0
                ? [new AgentSessionGroup(NotPartOfAPlan, untagged)]
                : Array.Empty<AgentSessionGroup>()
        ];
    }

    /// <summary>The heading over the desktop app's scheduled runs; see
    /// <see cref="ScheduledApart"/>.</summary>
    public const string Scheduled = "Scheduled";

    /// <summary>The key of the section <see cref="ScheduledApart"/> closes the list
    /// with, which a surface keys its fold on.</summary>
    public const string ScheduledKey = "scheduled";

    /// <summary>
    /// The same sections, with every row whose session is a scheduled task's run
    /// (<see cref="AgentSession.ScheduledTask"/>) taken out of the section it was in and
    /// put in one trailing <see cref="Scheduled"/> section, most recently active first.
    /// <para>
    /// A step after the grouping rather than a grouping of its own, because it applies
    /// under every grouping: the routine runs are a crowd a reader steps over whichever
    /// way the list is carved, and on the machine this was built against they were 99 of
    /// 132 open sessions. It moves rows and never drops one, so every row is still in
    /// exactly one section; a section the move left empty is dropped, as no grouping
    /// answers an empty one. With nothing scheduled the sections come back as they were.
    /// </para>
    /// </summary>
    public static IReadOnlyList<AgentSessionGroup> ScheduledApart(IReadOnlyList<AgentSessionGroup> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);

        static bool IsScheduled(SessionRow row) => !string.IsNullOrWhiteSpace(row.Session?.ScheduledTask);

        if (!sections.Any(section => section.Rows.Any(IsScheduled))) return sections;

        return
        [
            .. sections
                .Select(section => section with { Rows = [.. section.Rows.Where(row => !IsScheduled(row))] })
                .Where(section => section.Rows.Count > 0),
            new AgentSessionGroup(
                Scheduled,
                [.. sections.SelectMany(section => section.Rows).Where(IsScheduled).OrderByDescending(row => row.LastActivityAt)],
                ScheduledKey)
        ];
    }

    /// <summary>The When buckets, in the order the list shows them: key and heading.</summary>
    private static readonly (string Key, string Name)[] WhenBuckets =
    [
        (StandingKey, Standing),
        ("live", "Live now"),
        ("today", "Earlier today"),
        ("yesterday", "Yesterday"),
        ("week", "Past week"),
        ("older", "Older")
    ];

    private static IReadOnlyList<AgentSessionGroup> ByWhen(IReadOnlyList<SessionRow> ordered, DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).Date);

        // Live is the session's state and not its day: a session stalled since
        // yesterday is still the one a reader opening this list is looking for. A
        // standing session (an artifact, a plan, a hold …) is kept above the days
        // until it is archived, and sits in Live now only while it runs.
        string Bucket(SessionRow row)
        {
            if (AgentSessionViews.IsLive(row.State)) return "live";
            if (SessionTitles.IsStanding(SessionTitles.Of(row).Purpose)) return StandingKey;

            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(row.LastActivityAt, zone).Date);
            var daysAgo = today.DayNumber - day.DayNumber;

            return daysAgo switch
            {
                <= 0 => "today",
                1 => "yesterday",
                < 7 => "week",
                _ => "older"
            };
        }

        var byBucket = ordered.ToLookup(Bucket, StringComparer.Ordinal);

        return
        [
            .. WhenBuckets
                .Where(bucket => byBucket[bucket.Key].Any())
                .Select(bucket => new AgentSessionGroup(bucket.Name, [.. byBucket[bucket.Key]], bucket.Key))
        ];
    }

    /// <summary>What an assistant is called on screen. Here rather than in the
    /// pane, so a group heading and a row's own cell cannot disagree.</summary>
    public static string Label(AgentSessionKind kind) => kind switch
    {
        AgentSessionKind.Claude => "Claude",
        AgentSessionKind.Copilot => "Copilot",
        _ => kind.ToString()
    };
}
