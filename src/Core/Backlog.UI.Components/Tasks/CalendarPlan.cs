using System.Globalization;

namespace Backlog.UI.Components.Tasks;

/// <summary>
/// One plan's window as the month calendar draws it: a bar from its first day to its
/// last, across as many week rows as it reaches.
/// </summary>
/// <param name="Id">The host's id for the plan.</param>
/// <param name="Title">What the bar says.</param>
/// <param name="Tag">The plan tag, bare; the tooltip says it with its <c>+</c>.</param>
/// <param name="Start">First day, inclusive.</param>
/// <param name="End">Last day, inclusive.</param>
/// <param name="DonePoints">The points of its finished work.</param>
/// <param name="TotalPoints">The points all its work registered.</param>
/// <param name="Band">The repository band colour, 1 to 5, while the host's repository
/// colours are showing; null draws the bar neutral grey. The host decides, so the
/// calendar never knows whether the colours are on.</param>
public sealed record CalendarPlan(
    string Id,
    string Title,
    string Tag,
    DateOnly Start,
    DateOnly End,
    int DonePoints = 0,
    int TotalPoints = 0,
    int? Band = null)
{
    /// <summary>What the tooltip and a screen reader say: the title, the plan tag, and
    /// the finished points of the total.</summary>
    public string Summary =>
        string.Create(CultureInfo.CurrentCulture, $"{Title} · +{Tag} · {DonePoints} of {TotalPoints} pts");
}

/// <summary>A milestone, drawn as a diamond chip on its day.</summary>
public sealed record CalendarMilestone(string Id, string Title, DateOnly On);

/// <summary>A plan with no window yet, waiting in the tray to be dropped on a day.</summary>
/// <param name="Tag">The plan tag, bare — what a drop reports.</param>
/// <param name="Title">What the tray says.</param>
/// <param name="TaskCount">How many tasks it holds.</param>
/// <param name="TotalEffort">The points those tasks registered.</param>
/// <param name="UnestimatedCount">How many registered none.</param>
public sealed record CalendarShelfPlan(
    string Tag,
    string Title,
    int TaskCount = 0,
    int TotalEffort = 0,
    int UnestimatedCount = 0);

/// <summary>A shelf plan dropped on a day: start its window there.</summary>
public sealed record CalendarPlanStart(string Tag, DateOnly Start);

/// <summary>
/// The part of one plan's bar inside one week row.
/// </summary>
/// <param name="Column">The weekday it starts on, Monday 1 to Sunday 7 — a CSS grid
/// column.</param>
/// <param name="Span">How many days of the week it covers.</param>
/// <param name="Lane">Which row of bars it sits in, from 1; overlapping plans take
/// lanes of their own.</param>
/// <param name="ContinuesBefore">The plan started in an earlier week; the bar opens
/// with "…".</param>
/// <param name="ContinuesAfter">The plan runs on into a later week.</param>
public sealed record CalendarPlanSegment(
    CalendarPlan Plan,
    int Column,
    int Span,
    int Lane,
    bool ContinuesBefore,
    bool ContinuesAfter)
{
    /// <summary>The bar's text: the title, after "…" when it carries on from the week
    /// before.</summary>
    public string Label => ContinuesBefore ? $"… {Plan.Title}" : Plan.Title;
}

/// <summary>
/// Splits plan windows into week rows and stacks the overlapping ones in lanes, apart
/// from the markup so it can be pinned down on its own.
/// </summary>
public static class CalendarPlanLanes
{
    /// <summary>
    /// The segments of <paramref name="plans"/> that fall in the week starting on the
    /// Monday <paramref name="weekStart"/>, with the lane each sits in, and how many
    /// lanes the week needs.
    /// <para>
    /// Earliest first, a longer bar before a shorter one starting the same day, ties in
    /// the order given; each takes the first lane free over all its days. A plan whose
    /// end is before its start draws nothing.
    /// </para>
    /// </summary>
    public static (IReadOnlyList<CalendarPlanSegment> Segments, int Lanes) Week(
        DateOnly weekStart,
        IReadOnlyList<CalendarPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);

        var weekEnd = weekStart.AddDays(6);
        var cut = plans
            .Select((plan, order) => (plan, order))
            .Where(entry => entry.plan.End >= entry.plan.Start && entry.plan.Start <= weekEnd && entry.plan.End >= weekStart)
            .Select(entry =>
            {
                var from = entry.plan.Start < weekStart ? weekStart : entry.plan.Start;
                var to = entry.plan.End > weekEnd ? weekEnd : entry.plan.End;
                var column = from.DayNumber - weekStart.DayNumber + 1;
                return (entry.plan, entry.order, column, span: to.DayNumber - from.DayNumber + 1);
            })
            .OrderBy(entry => entry.column)
            .ThenByDescending(entry => entry.span)
            .ThenBy(entry => entry.order)
            .ToList();

        // The last column each lane is taken up to.
        var lanes = new List<int>();
        var segments = new List<CalendarPlanSegment>(cut.Count);

        foreach (var (plan, _, column, span) in cut)
        {
            var lane = lanes.FindIndex(last => last < column);
            if (lane < 0)
            {
                lanes.Add(0);
                lane = lanes.Count - 1;
            }

            lanes[lane] = column + span - 1;
            segments.Add(new CalendarPlanSegment(
                plan,
                column,
                span,
                lane + 1,
                ContinuesBefore: plan.Start < weekStart,
                ContinuesAfter: plan.End > weekEnd));
        }

        return (segments, lanes.Count);
    }
}
