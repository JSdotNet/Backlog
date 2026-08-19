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
/// What the dashboard is currently looking at: one repository or all of them,
/// over one of the two windows.
/// <para>
/// This is query state, not a preference. Nothing persists it — closing the
/// dashboard forgets it, which is the whole reason the surface has no settings:
/// there is no saved arrangement to get out of step with the code.
/// </para>
/// <para>
/// <see cref="RepositoryAlias"/> is null for "all repositories". A null is the
/// absence of a focus rather than a special repository called all, so a part
/// that scopes by repository can branch on it and a part that cannot scope at
/// all — every cost part — can ignore it without pretending.
/// </para>
/// </summary>
public sealed record DashboardScope(string? RepositoryAlias = null, DashboardPeriod Period = DashboardPeriod.TwelveWeeks)
{
    /// <summary>What the dashboard opens on: everything, over a quarter.</summary>
    public static DashboardScope Default { get; } = new();

    /// <summary>True when no single repository is in focus.</summary>
    public bool IsAllRepositories => string.IsNullOrWhiteSpace(RepositoryAlias);

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
