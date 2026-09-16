using Backlog.Modules.Dashboard.Abstractions.Insights;

namespace Backlog.Modules.Dashboard.Abstractions.Services;

/// <summary>
/// One assistant session, reduced to what a productivity figure is worked out from.
/// </summary>
/// <param name="MachineId">Which machine it ran on — the stable id, which is what
/// <see cref="DashboardScope.MachineId"/> is matched against.</param>
/// <param name="MachineName">What that machine is called, for a breakdown row to be
/// readable. Carried beside the id rather than looked up, so a machine whose name has
/// since changed still reads correctly in the row its own sessions produced.</param>
/// <param name="Assistant">Claude, Copilot — whatever the source calls the tool. A
/// string rather than an enum: this module does not own the list of assistants and
/// adding one must not be a change to this contract.</param>
/// <param name="StartedAt">
/// Null where the source could not date the start. It is a different fact from a
/// zero-length session, and the difference is visible on screen: a session with no
/// start counts towards how many there were and adds nothing to how long they ran.
/// </param>
/// <param name="LastActivityAt">Always known — at worst a file's own timestamp.</param>
/// <remarks>
/// Nothing here says whether a session is still going. The surface this feeds reports on
/// a window that is mostly in the past, and every figure on it counts a session that ran
/// the same whether or not it is still running — so liveness would be a field carried,
/// mapped and asserted for no reader. The session list is where "what is going on right
/// now" is answered, and it asks the Sessions context directly.
/// </remarks>
public sealed record AssistantSession(
    string MachineId,
    string MachineName,
    string Assistant,
    DateTimeOffset? StartedAt,
    DateTimeOffset LastActivityAt)
{
    /// <summary>
    /// The session's own identifier, as the assistant that wrote it calls it, so that a
    /// session row and an activity record are the same session by identity.
    /// <para>
    /// Beside the primary constructor rather than in it, the escape
    /// <c>AssistantSessionsInsight.SessionsPerWeek</c> already documents: fixtures and
    /// adapters construct this record positionally, and a sixth parameter would break
    /// every one of them to add something most of them have no opinion about.
    /// </para>
    /// <para>
    /// Carried for exactly one figure and unavoidable for it. The count of sessions
    /// that left no activity record at all is the difference between two lists, and a
    /// difference needs a key — without this there is no join and the figure cannot be
    /// computed rather than merely being harder to compute. Empty where a source has
    /// nothing to offer, which reads as "no activity record" and is the honest answer
    /// for a session nothing can be matched to.
    /// </para>
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// How many prompts the person sent in this session, where the source could count
    /// them, and null where it could not.
    /// <para>
    /// Null and never 0 standing in for absent. Only one of the two assistants leaves a
    /// transcript this can be counted from, so a figure that treated a missing count as
    /// zero would halve the average with sessions that said nothing about it. Anything
    /// that averages these skips the nulls, and says how many it skipped.
    /// </para>
    /// <para>
    /// An init property beside <see cref="Id"/> for the reason that one is: the
    /// positional constructor is the shape every fixture builds, and most of them have
    /// no opinion about this.
    /// </para>
    /// </summary>
    public int? Prompts { get; init; }
}

/// <summary>
/// Everything one read of the assistant sessions produced, including what it could not
/// read and what it had to leave out.
/// </summary>
/// <param name="Sessions">The sessions the source will describe.</param>
/// <param name="Unreadable">The sources that could not be read, by name, so the
/// surface can say which half of the picture is missing rather than presenting the
/// other half as the whole.</param>
/// <param name="Capped">True when the source stopped short of everything it holds, so
/// every figure derived from it is a floor rather than a total.</param>
/// <param name="CapPerAssistant">
/// How many sessions per assistant the source will describe at most. Carried rather than
/// known, because the cap is the source's and a surface that hard-coded the same number
/// would be a second copy of it free to drift — the sentence on screen says "the newest
/// N", and N has to be whatever the source actually stopped at.
/// </param>
public sealed record AssistantSessionReport(
    IReadOnlyList<AssistantSession> Sessions,
    IReadOnlyList<string> Unreadable,
    bool Capped,
    int CapPerAssistant)
{
    public static AssistantSessionReport Empty { get; } = new([], [], false, 0);
}

/// <summary>
/// PORT — the assistant sessions this installation can account for.
/// <para>
/// No window and no machine in the signature, unlike <see cref="IActivitySource"/>,
/// and that is a fact about the source rather than an omission. This one is a local
/// read that already returns everything it will ever return — capped per assistant,
/// which it says — so a window parameter would be a narrowing the source would have to
/// implement twice: once here and once in the module, where the scoping actually has
/// to happen anyway. Scoping is the module's pure derivation over one report, which is
/// also what makes changing the machine or the window cost no read at all.
/// </para>
/// <para>
/// Nothing in this contract names a Sessions type. The adapter that answers it may see
/// both contexts; nothing in this module may.
/// </para>
/// </summary>
public interface IAssistantSessionSource
{
    /// <summary>Whether this source can answer, and why not when it cannot.</summary>
    Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>Everything the source has, in one read.</summary>
    Task<AssistantSessionReport> GetSessionsAsync(CancellationToken cancellationToken = default);
}
