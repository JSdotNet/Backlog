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
/// <b>Read, not stored.</b> An item the import placed by its effort keeps the start the
/// plan stores, and its end is derived each time it is read, from the effort its tasks
/// register now, the pace in use now and the week the pace is counted in (local ADR
/// 0018, 0019). A pace or week change therefore moves every such bar without writing the
/// plan — a plan write would carry a newer stamp to the other PCs and overwrite an edit
/// made there in the same interval — and a PC that pulls the pace draws the bars the PC
/// that set it draws.
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
    /// <paramref name="item"/> as it reads now: when it is sized by its effort, gathers
    /// work and is not finished, its stored start — moved to the next worked day when it
    /// is not one — and an end derived from the effort gathered at the pace in use for
    /// its repositories, counted in the paces' working week — the window the import's own
    /// placement would store. Anything else is handed back as stored: a due date or a
    /// window a person placed is theirs, an item that gathers nothing has no effort to
    /// read, and a finished one is history.
    /// </summary>
    public static RoadmapItemDto Derive(RoadmapItemDto item, RoadmapItemRollupDto? rollup, PacesInUseDto paces)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(paces);

        if (!IsDerived(item)) return item;
        if (rollup is null || rollup.BacklogEntries.Count == 0 || rollup.IsFinished) return item;

        var pace = paces.For(item.RepositoryAliases);
        if (pace <= 0) return item;

        var start = FirstWorkedDay(item.Start, paces.Week);
        var end = EndFrom(start, Math.Max(0, rollup.TotalEffort), pace, paces.Week);
        return start == item.Start && end == item.End ? item : item with { Start = start, End = end };
    }

    /// <summary>Every item of <paramref name="items"/> as it reads now; see
    /// <see cref="Derive(RoadmapItemDto, RoadmapItemRollupDto?, PacesInUseDto)"/>.</summary>
    public static IReadOnlyList<RoadmapItemDto> Derive(
        IEnumerable<RoadmapItemDto> items,
        IReadOnlyDictionary<Guid, RoadmapItemRollupDto> rollups,
        PacesInUseDto paces)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(rollups);

        return [.. items.Select(item => Derive(item, rollups.GetValueOrDefault(item.Id), paces))];
    }

    /// <summary>
    /// The plan with every window sized by its effort read as it stands now — for a
    /// reader that reports the plan rather than draws it. Only the items sized by
    /// their effort are gathered, because gathering walks the backlog per item, and
    /// the paces are read once; a plan with none reads nothing more.
    /// <para>
    /// The contradictions are left as the plan worked them out, over the stored
    /// windows — the same the roadmap draws.
    /// </para>
    /// </summary>
    public static async Task<RoadmapPlanDto> WithDerivedWindowsAsync(
        this RoadmapPlanDto plan,
        IRoadmapItemRollup rollups,
        IPlanningVelocity velocity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(rollups);
        ArgumentNullException.ThrowIfNull(velocity);

        var sized = plan.Items.Where(IsDerived).ToList();
        if (sized.Count == 0) return plan;

        var gathered = await rollups.GatherPlanAsync(plan with { Items = sized }, cancellationToken).ConfigureAwait(false);
        var paces = await velocity.ReadPacesInUseAsync(cancellationToken).ConfigureAwait(false);

        return plan with { Items = Derive(plan.Items, gathered, paces) };
    }
}
