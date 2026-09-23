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

    /// <summary>
    /// <c>owner/name</c> where the assistant recorded it against the session, or —
    /// where it recorded none — where the Sessions context placed the session's working
    /// folder inside a registered clone; null where neither says. Claude writes a
    /// working folder and no repository, so its sessions arrive here by the second
    /// route or not at all. The recorded one wins where both exist, because it is the
    /// session's own fact and the placed one is only as good as the registration.
    /// Null crosses as null: a surface grouping on this shows those sessions as one
    /// band named for the fact rather than dropping them or attributing them to a
    /// guess.
    /// <para>An init property on <see cref="Prompts"/>' precedent.</para>
    /// </summary>
    public string? Repository { get; init; }

    /// <summary>
    /// The pull requests the session linked itself to, or null where the source cannot
    /// say — Copilot records none, and an unreadable transcript says nothing. Empty is
    /// a reading that found none. Anything that counts these skips the nulls and says
    /// the figure is partial, on <see cref="Prompts"/>' rule.
    /// </summary>
    public IReadOnlyList<AssistantPullRequest>? PullRequests { get; init; }

    /// <summary>
    /// What the session spent per model, on <see cref="PullRequests"/>' null-and-empty
    /// terms. The owner session's own spend: the agents it spawned are not in it.
    /// </summary>
    public IReadOnlyList<AssistantModelUsage>? ModelUsage { get; init; }
}

/// <summary>A pull request a session linked itself to, in the Dashboard's words.</summary>
/// <param name="Repository"><c>owner/name</c>, as the assistant spelled it.</param>
/// <param name="Number">The number in that repository.</param>
/// <param name="Url">Where it lives — and what makes two sessions' links one pull
/// request, so a pull request several sessions linked is counted once.</param>
/// <param name="LinkedAt">When the session linked it, where that was dated.</param>
public sealed record AssistantPullRequest(string Repository, int Number, string Url, DateTimeOffset? LinkedAt);

/// <summary>What one session spent on one model, in tokens.</summary>
public sealed record AssistantModelUsage(
    string Model,
    long InputTokens,
    long OutputTokens,
    long CacheCreationInputTokens,
    long CacheReadInputTokens);

/// <summary>
/// Everything one read of the assistant sessions produced, including what it could not
/// read and what it had to leave out.
/// </summary>
/// <param name="Sessions">The sessions the source will describe.</param>
/// <param name="Unreadable">The sources that could not be read, by name, so the
/// surface can say which half of the picture is missing rather than presenting the
/// other half as the whole.</param>
/// <param name="Capped">True when the source could not reach as far back as it was
/// asked, so every figure derived from it is a floor rather than a total.</param>
public sealed record AssistantSessionReport(
    IReadOnlyList<AssistantSession> Sessions,
    IReadOnlyList<string> Unreadable,
    bool Capped)
{
    public static AssistantSessionReport Empty { get; } = new([], [], false);
}

/// <summary>
/// PORT — the assistant sessions this installation can account for.
/// <para>
/// A horizon and no machine in the signature, on <see cref="IAssistantActivitySource"/>'s
/// pattern. The horizon is the source's business because it decides what the source
/// reads at all: without one the Sessions context answers with its inventory, which is
/// the newest hundred per assistant, and a count over that list is a page size wearing a
/// total's clothes — 200 on every machine, which is what this part showed. The machine
/// stays out of it for the reason the activity port gives: the module's scoping is a
/// pure derivation over one report, which is what makes moving the machine filter or the
/// period control cost no read at all.
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

    /// <summary>Every session whose last activity is at or after <paramref name="since"/>,
    /// in one read. Uncapped inside the horizon; <see cref="AssistantSessionReport.Capped"/>
    /// says when the source could not reach that far.</summary>
    Task<AssistantSessionReport> GetSessionsAsync(DateTimeOffset since, CancellationToken cancellationToken = default);
}
