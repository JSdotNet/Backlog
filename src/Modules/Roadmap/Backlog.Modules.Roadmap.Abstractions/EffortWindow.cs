using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Modules.Roadmap.Abstractions;

/// <summary>
/// The length of a window sized by its effort: the gathered effort over the pace, a
/// week being seven days — rounded up, never under <see cref="MinimumSpanDays"/>, and
/// <see cref="DefaultSpanDays"/> when nothing estimated was gathered (ADR 0013, ruling
/// 4). Days are calendar days, because the roadmap models no working week.
/// <para>
/// One formula for the two places a window is sized: the import that places an item
/// and stores its window, and every reader that draws or reports one still sized by
/// its effort. The second is why it lives here rather than in the module: the roadmap
/// view, the dashboard, the MCP tool and the assistant's content read the plan through
/// the abstractions and cannot reach the module's own code.
/// </para>
/// <para>
/// <b>Read, not stored.</b> An item the import placed by its effort keeps the start the
/// plan stores, and its end is derived each time it is read, from the effort its tasks
/// register now and the pace in use now (local ADR 0018). A pace change therefore moves
/// every such bar without writing the plan — a plan write would carry a newer stamp to
/// the other PCs and overwrite an edit made there in the same interval — and a PC that
/// pulls the pace draws the bars the PC that set it draws.
/// </para>
/// </summary>
public static class EffortWindow
{
    /// <summary>The shortest window a length can make: a window is at least a day.</summary>
    public const int MinimumSpanDays = 1;

    /// <summary>The span when the total would be a number invented from nothing — a
    /// working week, the smallest span that reads as a plan rather than a day.</summary>
    public const int DefaultSpanDays = 5;

    /// <summary>How many calendar days a gathered total spans at a velocity in story
    /// points a week.
    /// <para>
    /// Multiplied by seven before dividing, never divided by a per-day figure: 4 a
    /// week is 0.571428… a day, which no decimal holds exactly, and 4 points over it
    /// would come to a hair over 7 days and round up to 8.
    /// </para>
    /// </summary>
    /// <param name="velocity">Story points per week; always positive.</param>
    public static int Days(int gatheredEffort, decimal velocity)
    {
        if (gatheredEffort <= 0) return DefaultSpanDays;
        if (velocity <= 0) throw new ArgumentOutOfRangeException(nameof(velocity), velocity, "Velocity is always positive.");

        var days = Math.Ceiling(gatheredEffort * 7m / velocity);
        return days >= int.MaxValue ? int.MaxValue : Math.Max(MinimumSpanDays, (int)days);
    }

    /// <summary>The last day of a window from <paramref name="start"/> that spans
    /// <see cref="Days"/>. Clamped so an absurd total cannot run the window off the
    /// calendar.</summary>
    public static DateOnly EndFrom(DateOnly start, int gatheredEffort, decimal velocity)
    {
        var lastDay = Math.Min((long)start.DayNumber + Days(gatheredEffort, velocity) - 1, DateOnly.MaxValue.DayNumber);
        return DateOnly.FromDayNumber((int)lastDay);
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
    /// work and is not finished, its stored start and an end derived from the effort
    /// gathered at the pace in use for its repositories — the window the import's own
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

        var end = EndFrom(item.Start, Math.Max(0, rollup.TotalEffort), pace);
        return end == item.End ? item : item with { End = end };
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
