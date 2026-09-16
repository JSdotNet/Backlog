namespace Backlog.Modules.Dashboard.Abstractions;

/// <summary>
/// How far back a part looks, in whole weeks.
/// <para>
/// Weeks rather than days, and a closed set rather than a number: a productivity
/// figure over a single day is mostly noise about which day of the week it was,
/// and an arbitrary window would make two readings taken a fortnight apart
/// incomparable. Four weeks is "recently", twelve is "this quarter".
/// </para>
/// </summary>
public enum DashboardPeriod
{
    FourWeeks,
    TwelveWeeks
}

/// <summary>
/// What the dashboard is currently looking at: some repositories or all of them, one
/// machine or all of them, over one of the two windows.
/// <para>
/// This is query state, not a preference. Nothing persists it — closing the
/// dashboard forgets it, which is the whole reason the surface has no settings:
/// there is no saved arrangement to get out of step with the code.
/// </para>
/// <para>
/// <see cref="Repositories"/> is <see cref="RepositoryFocus.All"/> for "all
/// repositories" and <see cref="MachineId"/> is null for "all machines". Each is
/// the absence of a focus rather than a special repository or machine called all,
/// so a part that scopes by one of them can branch on it and a part that cannot
/// scope at all — every cost part, and every GitHub-backed part where the machine
/// is concerned — can ignore it without pretending.
/// </para>
/// <para>
/// The repositories are a set, not one alias, because the focus is the shell's
/// repository scope and that scope takes several at once. A part that can only
/// name one takes the set's anchor.
/// </para>
/// <para>
/// The two focuses are independent and neither part of the surface honours both.
/// GitHub does not report which machine a pull request was worked from, and Claude
/// records no repository against a session, so a dimension a part cannot answer for
/// is stated on that part rather than quietly ignored — see
/// <c>DashboardPartBase.FollowsRepository</c> and <c>FollowsMachine</c>.
/// </para>
/// <para>
/// <see cref="MachineId"/> is appended last on purpose: it keeps every existing
/// positional construction meaning what it already meant.
/// </para>
/// </summary>
public sealed record DashboardScope(
    RepositoryFocus? Repositories = null,
    DashboardPeriod Period = DashboardPeriod.TwelveWeeks,
    string? MachineId = null)
{
    /// <summary>What the dashboard opens on: every repository, every machine, over a
    /// quarter.</summary>
    public static DashboardScope Default { get; } = new();

    /// <summary>The repositories in focus. Never null: a caller that passes none
    /// gets <see cref="RepositoryFocus.All"/>, so "not narrowed" has one spelling.</summary>
    public RepositoryFocus Repositories { get; init; } = Repositories ?? RepositoryFocus.All;

    /// <summary>True when no repository is in focus.</summary>
    public bool IsAllRepositories => Repositories.IsAll;

    /// <summary>True when no single machine is in focus.</summary>
    public bool IsAllMachines => string.IsNullOrWhiteSpace(MachineId);

    /// <summary>The window as whole weeks.</summary>
    public int Weeks => Period == DashboardPeriod.FourWeeks ? 4 : 12;

    /// <summary>
    /// The window as a half-open instant range ending at <paramref name="now"/>,
    /// aligned to the start of a day so two calls a minute apart bucket
    /// identically. A caller passes its own clock: nothing in this module reads
    /// one, so a test can ask for a fixed window.
    /// </summary>
    public (DateTimeOffset From, DateTimeOffset To) Window(DateTimeOffset now)
    {
        var to = new DateTimeOffset(now.Date, now.Offset);
        return (to.AddDays(-7 * Weeks), now);
    }
}
