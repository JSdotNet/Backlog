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
    Finished
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
/// One assistant session, as far as the machine it ran on can describe it.
/// </summary>
/// <param name="Id">The agent's own identifier for the session.</param>
/// <param name="Kind">Which assistant.</param>
/// <param name="Environment">
/// Where the session ran. This context's own term, and deliberately not the same
/// word as Dev PC Management's Machine: an environment is wherever an agent can run
/// — a development PC today, and there is nothing in this model that stops it being
/// a container or a hosted runner tomorrow. It corresponds to a registered Machine
/// when the two happen to name the same box, which is a
/// <c>Customer/Supplier</c> lookup rather than a shared identity; see
/// <c>.domain/sessions/dependencies.md</c>.
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
public sealed record AgentSession(
    string Id,
    AgentSessionKind Kind,
    string Environment,
    string Title,
    string WorkingFolder,
    string? Repository,
    string? Branch,
    DateTimeOffset? StartedAt,
    DateTimeOffset LastActivityAt,
    AgentSessionState State);

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
/// <param name="Sessions">What the source will describe, up to
/// <see cref="AgentSessionLimits.PerAgent"/> from each agent.</param>
/// <param name="Unreadable">The sources that could not be read, by name.</param>
/// <param name="Discovered">
/// How many sessions existed, before the cap. Sessions and not files: an agent can
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
}

/// <summary>
/// Where the sessions on a machine come from. The port; whether it reads local
/// files, asks a registered machine, or answers from a fixture is the host's
/// business.
/// </summary>
public interface IAgentSessionSource
{
    Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default);
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

    /// <summary>Everything the source described, evidence or not.</summary>
    All
}

/// <summary>
/// Choosing which sessions to show. A pure function over the sessions it is given,
/// the same way <see cref="AgentSessionGroups"/> is — and the composition is view
/// first, then grouping: a surface filters the list and groups what survived, never
/// the other way round, so a machine with nothing live on it loses its section
/// rather than keeping an empty one.
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
    /// The sessions this view admits, in the order they were given. Ordering is the
    /// grouping's job; a filter that also sorted would be a second answer to what
    /// "most recently active first" means.
    /// </summary>
    public static IReadOnlyList<AgentSession> Of(IReadOnlyList<AgentSession> sessions, AgentSessionView view)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        return view switch
        {
            AgentSessionView.Live => [.. sessions.Where(IsLive)],

            // The same list back, not a copy of it: All is the absence of a filter,
            // and rebuilding the list would be work done to change nothing.
            _ => sessions
        };
    }

    /// <summary>
    /// Whether the reading called this session still going.
    /// <para>
    /// Public deliberately, and the only place this product spells the Session Log's
    /// invariant out in code: Running and Stalled both require liveness evidence,
    /// and with none a session is Finished — see <c>.domain/sessions/domain.md</c>.
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
    public static bool IsLive(AgentSession session) =>
        session.State is AgentSessionState.Running or AgentSessionState.Stalled;

    /// <summary>What a view is called on screen. Here rather than in the pane, for
    /// the reason <see cref="AgentSessionGroups.Label(AgentSessionKind)"/> is: a
    /// control and a sentence about it cannot disagree if there is one word.</summary>
    public static string Label(AgentSessionView view) => view switch
    {
        AgentSessionView.Live => "Live",
        _ => "All"
    };
}

/// <summary>How the reader wants the list carved up.</summary>
public enum AgentSessionGrouping
{
    /// <summary>One flat list, most recently active first.</summary>
    None,

    /// <summary>A section per machine.</summary>
    Environment,

    /// <summary>A section per assistant.</summary>
    Kind
}

/// <summary>
/// One section of a grouped list. <c>Name</c> is null for the ungrouped case, so a
/// caller renders sections and never has to branch on the grouping again.
/// </summary>
public sealed record AgentSessionGroup(string? Name, IReadOnlyList<AgentSession> Sessions);

/// <summary>
/// Carving the list up. A pure function over the sessions it is given: no I/O, no
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
        IReadOnlyList<AgentSession> sessions,
        AgentSessionGrouping grouping)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var ordered = sessions.OrderByDescending(session => session.LastActivityAt).ToList();

        return grouping switch
        {
            AgentSessionGrouping.Environment =>
            [
                .. ordered
                    .GroupBy(session => session.Environment, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new AgentSessionGroup(group.Key, [.. group]))
            ],
            AgentSessionGrouping.Kind =>
            [
                .. ordered
                    .GroupBy(session => session.Kind)
                    .OrderBy(group => group.Key)
                    .Select(group => new AgentSessionGroup(Label(group.Key), [.. group]))
            ],
            _ => ordered.Count == 0 ? [] : [new AgentSessionGroup(null, ordered)]
        };
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
