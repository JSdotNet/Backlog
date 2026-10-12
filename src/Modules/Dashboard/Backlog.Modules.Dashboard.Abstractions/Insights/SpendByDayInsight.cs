namespace Backlog.Modules.Dashboard.Abstractions.Insights;

/// <summary>
/// One day of the calendar month on the Cost tab's spend-over-time chart.
/// </summary>
/// <param name="Date">The day.</param>
/// <param name="Amount">What the providers spent that day, or — for a projected day —
/// what they are expected to: the sum of their average daily spend over the last seven
/// days, the same average <see cref="SpendProjection"/> projects the month-end from.</param>
/// <param name="IsProjected">True for a day after today. Drawn apart, so a projection never
/// reads as spend that happened.</param>
public sealed record SpendDay(DateOnly Date, decimal Amount, bool IsProjected);

/// <summary>
/// Spend per day across the whole calendar month: the days so far as reported, the days
/// after today as projected, and the budget the month is set against.
/// </summary>
/// <remarks>
/// <para>
/// One amount per day across the providers that answered, because the chart answers
/// "will this month stay inside its budget" and the budget line it draws is the sum of the
/// providers' budgets. The figures are added only when every provider reports in one
/// currency; across two the derivation refuses, as every other cost figure here does, and
/// the part says why instead of drawing a total in no currency at all.
/// </para>
/// <para>
/// <see cref="Budget"/> is the sum of the budgets of the providers that answered and have
/// one, and null when none has — then there is no line to draw. A provider with no budget
/// still adds its spend to the bars; the line is the budget there is, not a claim that the
/// month's whole spend is covered by it.
/// </para>
/// </remarks>
public sealed record SpendByDayInsight(
    IReadOnlyList<SpendDay> Days,
    string Currency,
    DashboardMoney? Budget,
    bool IsEstimate)
{
    public static SpendByDayInsight Empty { get; } = new([], "USD", null, false);
}
