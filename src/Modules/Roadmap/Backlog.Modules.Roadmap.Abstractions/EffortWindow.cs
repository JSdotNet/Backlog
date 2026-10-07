using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Modules.Roadmap.Abstractions;

/// <summary>
/// The window an effort sizes: the hours it needs, counted forward through the person's
/// working week from its start (local ADR 0019, amending ADR 0013 ruling 4).
/// <para>
/// The hours needed are the gathered effort × the week's hours ÷ the pace in story points
/// a working week. From the start — moved to the next worked day when it is not one —
/// each worked day takes its whole hours off what is left, and the window ends on the day
/// nothing is left. A day counts whole, so a window that starts today counts all of
/// today. Days not worked add nothing and are still part of the window, so a window
/// always runs from one worked day to another. Nothing gathered, or nothing estimated,
/// needs one working week of hours. A week with no working hours at all reads as
/// <see cref="WorkingHours.Default"/>.
/// </para>
/// <para>
/// One formula for every place a window is sized: the import that places an item and
/// stores its window, and every reader that draws or reports one still sized by its
/// effort. The second is why it lives here rather than in the module: the roadmap view,
/// the dashboard, the MCP tool and the assistant's content read the plan through the
/// abstractions and cannot reach the module's own code.
/// </para>
/// <para>
/// <b>Shared, and stored on opening.</b> An item the import placed by its effort is laid
/// out again from today by the keep-up projection (<see cref="RoadmapProjection"/>): part
/// by part, from the effort its tasks register now, at the pace in use now, counted in the
/// week the pace is counted in (ADR 0013, ruling 5 as amended on 2026-09-27 and
/// 2026-10-07; local ADR 0018, 0019). The roadmap stores the window it moves when it opens
/// and when a task changes — a projection that moves nothing writes nothing — and every
/// reader here applies the same rule, so one that has not opened the roadmap today still
/// reports the dates it would store. A pace or week change never writes: it redraws every
/// such window, and the stored one catches up at the next opening or task change, because
/// a plan write carries a newer stamp to the other PCs and would overwrite an edit made
/// there in the same interval.
/// </para>
/// </summary>
public static class EffortWindow
{
    /// <summary>The first worked day on or after <paramref name="day"/> — where a window
    /// from <paramref name="day"/> starts.</summary>
    public static DateOnly FirstWorkedDay(DateOnly day, WorkingHours week)
    {
        ArgumentNullException.ThrowIfNull(week);
        return week.FirstWorkedDay(day);
    }

    /// <summary>
    /// The last day of a window from <paramref name="start"/> that a gathered total
    /// needs at <paramref name="velocity"/>, counted in <paramref name="week"/>
    /// (<see cref="WorkingHours.LastDayOf(DateOnly, decimal, decimal)"/>).
    /// <para>
    /// The effort is multiplied by the week's length before dividing by the pace, never
    /// divided by a per-hour figure: 4 points at 4 a week is then exactly one week of
    /// hours, rather than a hair over it that spills into the next worked day.
    /// </para>
    /// </summary>
    /// <param name="gatheredEffort">The story points gathered; zero or less is nothing
    /// estimated, which needs one working week.</param>
    /// <param name="velocity">Story points per working week; always positive.</param>
    public static DateOnly EndFrom(DateOnly start, int gatheredEffort, decimal velocity, WorkingHours week)
    {
        ArgumentNullException.ThrowIfNull(week);
        if (velocity <= 0) throw new ArgumentOutOfRangeException(nameof(velocity), velocity, "Velocity is always positive.");

        return gatheredEffort <= 0
            ? week.LastDayOf(start, week.Effective.PerWeek)
            : week.LastDayOf(start, gatheredEffort, velocity);
    }

    /// <summary>The longest a forecast of work in flight is drawn, in calendar days —
    /// ten years, so a stray estimate cannot stretch a bar across the calendar.</summary>
    public const int LongestForecastDays = 3650;

    /// <summary>
    /// Where the work left on an item already in flight should be done: the
    /// <paramref name="openEffort"/> left, counted from <paramref name="openFrom"/> at
    /// <paramref name="velocity"/> through the working week, the way a window is. Unlike
    /// a window, nothing left is not a default week: the open work ends on the first
    /// worked day it can be drawn on. Never more than <see cref="LongestForecastDays"/>.
    /// </summary>
    /// <param name="velocity">Story points per working week; always positive.</param>
    public static DateOnly ForecastEnd(DateOnly openFrom, int openEffort, decimal velocity, WorkingHours week)
    {
        ArgumentNullException.ThrowIfNull(week);

        var end = openEffort <= 0
            ? week.FirstWorkedDay(openFrom)
            : EndFrom(openFrom, openEffort, velocity, week);

        var latest = Math.Min((long)openFrom.DayNumber + LongestForecastDays - 1, DateOnly.MaxValue.DayNumber);
        return end.DayNumber > latest ? DateOnly.FromDayNumber((int)latest) : end;
    }

    /// <summary>The last day of a window from <paramref name="start"/> that needs
    /// <paramref name="needed"/> working time.</summary>
    public static DateOnly EndAfter(DateOnly start, TimeSpan needed, WorkingHours week)
    {
        ArgumentNullException.ThrowIfNull(week);
        return week.LastDayOf(start, needed);
    }

    /// <summary>Whether <paramref name="item"/>'s end is derived when it is read — the
    /// import placed it by its effort, and nobody has placed it since.</summary>
    public static bool IsDerived(RoadmapItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.PlacedByImport is ImportPlacement.Effort;
    }

    /// <summary>
    /// <paramref name="item"/> as it reads on <paramref name="today"/>: when the projection
    /// keeps it up — sized by its effort, its end not pinned, work gathered and not all done
    /// — laid out part by part from the later of today and <paramref name="floor"/>, its
    /// window the parts' envelope (<see cref="RoadmapProjection.One"/>). Anything else is
    /// handed back as stored: a due date or a window a person placed is theirs, an item that
    /// gathers nothing has no effort to read, and a finished one is history.
    /// </summary>
    /// <param name="floor">The day after the item's latest predecessor ends; today when not
    /// given. A reader holding the whole plan reads it through
    /// <see cref="RoadmapProjection.Project"/> instead, which works the floors out.</param>
    public static RoadmapItemDto Derive(
        RoadmapItemDto item,
        RoadmapItemRollupDto? rollup,
        PacesInUseDto paces,
        DateOnly today,
        DateOnly? floor = null) =>
        RoadmapProjection.One(item, rollup, paces, today, floor).Item;

    /// <summary>Every item of <paramref name="items"/> as it reads on
    /// <paramref name="today"/>, each after what it waits on as that now stands
    /// (<see cref="RoadmapProjection.Project"/>).</summary>
    public static IReadOnlyList<RoadmapItemDto> Derive(
        IReadOnlyList<RoadmapItemDto> items,
        IReadOnlyDictionary<Guid, RoadmapItemRollupDto> rollups,
        PacesInUseDto paces,
        DateOnly today,
        IReadOnlyList<RoadmapMilestoneDto>? milestones = null) =>
        RoadmapProjection.Project(items, rollups, paces, today, milestones);

    /// <summary>
    /// The plan with every window sized by its effort read as the projection lays it out on
    /// <paramref name="today"/> — for a reader that reports the plan rather than draws it.
    /// Only the items sized by their effort are gathered, because gathering walks the
    /// backlog per item, and the paces are read once; a plan with none reads nothing more.
    /// <para>
    /// The contradictions are left as the plan worked them out, over the stored
    /// windows.
    /// </para>
    /// </summary>
    public static Task<RoadmapPlanDto> WithDerivedWindowsAsync(
        this RoadmapPlanDto plan,
        IRoadmapItemRollup rollups,
        IPlanningVelocity velocity,
        DateOnly today,
        CancellationToken cancellationToken = default) =>
        plan.WithDerivedWindowsAsync(rollups, velocity, today, _ => true, cancellationToken);

    /// <summary>
    /// The plan with the windows of the items <paramref name="reading"/> picks read as the
    /// projection lays them out on <paramref name="today"/> — for a reader that reports one
    /// slice of the plan. Each is still read after what it waits on: the predecessors it
    /// reaches are projected with it, and only the items sized by their effort among them
    /// are gathered. Every other item is handed back as stored.
    /// </summary>
    public static async Task<RoadmapPlanDto> WithDerivedWindowsAsync(
        this RoadmapPlanDto plan,
        IRoadmapItemRollup rollups,
        IPlanningVelocity velocity,
        DateOnly today,
        Func<RoadmapItemDto, bool> reading,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(rollups);
        ArgumentNullException.ThrowIfNull(velocity);
        ArgumentNullException.ThrowIfNull(reading);

        var reached = Reached(plan.Items, reading);
        var sized = reached.Where(IsDerived).ToList();
        if (sized.Count == 0) return plan;

        var gathered = await rollups.GatherPlanAsync(plan with { Items = sized }, cancellationToken).ConfigureAwait(false);
        var paces = await velocity.ReadPacesInUseAsync(cancellationToken).ConfigureAwait(false);

        var read = Derive(reached, gathered, paces, today, plan.Milestones).ToDictionary(item => item.Id);
        return plan with { Items = [.. plan.Items.Select(item => read.GetValueOrDefault(item.Id, item))] };
    }

    /// <summary>The items <paramref name="reading"/> picks and every item they wait on,
    /// directly or not, in the plan's order.</summary>
    private static List<RoadmapItemDto> Reached(IReadOnlyList<RoadmapItemDto> items, Func<RoadmapItemDto, bool> reading)
    {
        var byId = items.DistinctBy(item => item.Id).ToDictionary(item => item.Id);
        var reached = new HashSet<Guid>();
        var pending = new Stack<Guid>(items.Where(reading).Select(item => item.Id));

        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (!reached.Add(id)) continue;

            foreach (var waited in byId[id].DependsOn.Where(byId.ContainsKey)) pending.Push(waited);
        }

        return [.. items.Where(item => reached.Contains(item.Id)).DistinctBy(item => item.Id)];
    }
}
