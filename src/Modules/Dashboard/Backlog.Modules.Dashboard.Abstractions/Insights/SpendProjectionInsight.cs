namespace Backlog.Modules.Dashboard.Abstractions.Insights;

/// <summary>
/// One provider's spend so far this calendar month, where it is heading by the
/// month's end, and the budget it is set against.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Projected"/> is <see cref="MonthToDate"/> plus <see cref="AverageDaily"/>
/// times <see cref="DaysLeft"/>. The average is over the last seven days of the month
/// so far, today included — or over every day of the month so far when fewer than
/// seven have passed, so the first of the month projects from the first day alone.
/// </para>
/// <para>
/// Every amount is in the provider's own currency. <see cref="Budget"/> is null when
/// the person set none for this provider, and then the provider is never projected
/// past it.
/// </para>
/// </remarks>
public sealed record SpendProjection(
    SpendProvider Provider,
    DashboardMoney MonthToDate,
    DashboardMoney AverageDaily,
    DashboardMoney Projected,
    DashboardMoney? Budget,
    DateOnly Through,
    int DaysLeft,
    bool IsEstimate)
{
    /// <summary>True when the provider has a budget and the month-end projection
    /// is above it.</summary>
    public bool IsProjectedPastBudget => Budget is { } budget && Projected.Amount > budget.Amount;
}

/// <summary>The month-end projection, per provider. A provider whose source could
/// not answer is absent rather than present as zero.</summary>
public sealed record SpendProjectionInsight(IReadOnlyList<SpendProjection> Providers)
{
    public static SpendProjectionInsight Empty { get; } = new([]);
}
