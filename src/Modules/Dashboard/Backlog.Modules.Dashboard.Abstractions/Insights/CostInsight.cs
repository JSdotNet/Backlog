namespace Backlog.Modules.Dashboard.Abstractions.Insights;

/// <summary>Which assistant a spend figure came from. Kept apart all the way to
/// the screen: the two providers report on different terms — one an estimate, one
/// a metered credit balance — and a single total would hide that.</summary>
public enum SpendProvider
{
    Claude,
    Copilot
}

/// <summary>
/// One provider's spend so far this calendar month.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Spend"/> is what was charged. <see cref="Allowance"/> is the part of
/// the month's consumption the plan already covered, which GitHub reports as a
/// discount against the gross figure and Anthropic does not report at all — so it
/// is null for Claude. The two together are what was consumed; neither is the
/// plan's total included credit, because no endpoint publishes that and inventing
/// a ceiling is how a meter starts lying.
/// </para>
/// <para>
/// <see cref="IsEstimate"/> is true when the provider calls its own figure an
/// estimate. Anthropic does, for the Claude Code report; GitHub's billing figures
/// are what it charges. A part that shows both says which is which rather than
/// adding them into one authoritative-looking total.
/// </para>
/// </remarks>
public sealed record MonthlySpend(
    SpendProvider Provider,
    DashboardMoney Spend,
    DashboardMoney? Allowance,
    DateOnly MonthStart,
    DateOnly Through,
    bool IsEstimate);

/// <summary>Spend this month, per provider. A provider whose source could not
/// answer is absent from the list rather than present as zero.</summary>
public sealed record SpendThisMonthInsight(IReadOnlyList<MonthlySpend> Providers)
{
    public static SpendThisMonthInsight Empty { get; } = new([]);
}

/// <summary>
/// Spend over time, one series per provider, bucketed by day within a month or by
/// month across a longer window.
/// </summary>
public sealed record SpendTrendInsight(IReadOnlyList<InsightSeries> ByProvider, string Currency, SpendBucket Bucket)
{
    public static SpendTrendInsight Empty { get; } = new([], "USD", SpendBucket.Day);
}

/// <summary>How wide a bucket a spend trend is drawn in.</summary>
public enum SpendBucket
{
    Day,
    Month
}

/// <summary>
/// Where the money went, by model, across both providers.
/// <para>
/// Rows carry the provider in their detail rather than being split into two
/// tables: the question a reader is asking here is which model costs the most,
/// and answering it across two tables makes them do the merge by eye.
/// </para>
/// </summary>
public sealed record SpendByModelInsight(IReadOnlyList<InsightRow> Rows)
{
    public static SpendByModelInsight Empty { get; } = new([]);
}
