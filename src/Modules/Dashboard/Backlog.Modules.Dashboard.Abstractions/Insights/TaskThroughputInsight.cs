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

    /// <summary>
    /// The story points of the tasks ticked off in the stretch of the same length just
    /// before the one <see cref="Effort"/> counts, under the same repository scope — or
    /// null when nothing was worked out: a window with no week in it at all, which
    /// answers with no columns, carries no previous figure either.
    /// <para>
    /// The days that end the day before the first column's Monday, and as many of them
    /// as <see cref="Effort"/> counts — that Monday through today, which is the window's
    /// weeks plus the days its first column was rounded out by. Both edges matter. The
    /// columns round their first week out to its Monday so it draws as a whole week, so
    /// a previous window that ran up to the window's own opening would count those days
    /// on both sides and pull every delta towards nothing; and one that was only the
    /// window's weeks long would be shorter than the span it is set beside, so a steady
    /// pace would read as growth.
    /// </para>
    /// <para>
    /// Null rather than zero when it was not worked out, because a zero is a reading —
    /// "nothing was done before" — and the tile would turn it into a delta against
    /// nothing. An init property rather than a fourth parameter, on the precedent the
    /// other insight records already set: fixtures build this one positionally.
    /// </para>
    /// </summary>
    public decimal? PreviousEffort { get; init; }
}
