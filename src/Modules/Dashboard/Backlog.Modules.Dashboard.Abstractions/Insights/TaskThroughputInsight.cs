namespace Backlog.Modules.Dashboard.Abstractions.Insights;

/// <summary>
/// What the person got through, per ISO week, in the repositories in scope.
/// </summary>
/// <param name="CompletedPerWeek">Tasks ticked off, one point per week of the window,
/// oldest first. Every week is present, a quiet one as zero.</param>
/// <param name="EffortPerWeek">The story points of those same tasks, on the same weeks.
/// An unestimated task adds nothing here and is counted in <paramref name="Unestimated"/>
/// instead, so the figure can say it is a floor.</param>
/// <param name="Unestimated">How many of the counted tasks carried no estimate.</param>
public sealed record TaskThroughputInsight(
    IReadOnlyList<InsightPoint> CompletedPerWeek,
    IReadOnlyList<InsightPoint> EffortPerWeek,
    int Unestimated)
{
    /// <summary>How many tasks the window counted.</summary>
    public int Completed => (int)CompletedPerWeek.Sum(point => point.Value);

    /// <summary>How many story points the window counted.</summary>
    public decimal Effort => EffortPerWeek.Sum(point => point.Value);
}
