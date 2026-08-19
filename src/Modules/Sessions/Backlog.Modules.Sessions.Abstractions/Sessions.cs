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
/// How many session records existed, before the cap. Carried so a capped list can
/// say so: a surface that showed 200 of 842 without mentioning the 842 would be
/// presenting a truncated list as the whole history, which is the one thing a
/// capped list must not do.
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
