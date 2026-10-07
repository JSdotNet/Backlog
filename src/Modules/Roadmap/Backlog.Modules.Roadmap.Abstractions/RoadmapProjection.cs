using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.Abstractions;

/// <summary>
/// The keep-up projection: the plan laid out again from today (ADR 0013, ruling 5 as
/// amended on 2026-09-27 and 2026-10-07).
/// <para>
/// The items are walked so that each comes after what it waits on. An item the import sized
/// by its effort — no pinned end, work gathered and not all of it done — is laid out part by
/// part (<see cref="RoadmapItemParts"/>) from the later of today and the day after its
/// predecessors end as they now stand: a part not begun from there for its effort, a begun
/// part from the day its work began for the points still open. Its window becomes the parts'
/// envelope, and it stays the import's. Every other item is handed back as stored — a due
/// date or a window a person placed is theirs, finished work is history, and an item that
/// gathers nothing has no effort to read — and its stored end still holds back what waits on
/// it.
/// </para>
/// <para>
/// One rule for the writer that stores the moved windows and for every reader that draws or
/// reports them, so a reader that has not opened the roadmap today still reports the dates
/// the roadmap would store. Pure: the same plan, work, paces and day give the same answer,
/// and projecting that answer again the same day moves nothing.
/// </para>
/// </summary>
public static class RoadmapProjection
{
    /// <summary>The day after the latest of <paramref name="predecessorEnds"/>, or
    /// <paramref name="today"/> when the item waits on nothing. Neither held to today nor
    /// moved to a worked day: the parts do both, for every caller alike.</summary>
    public static DateOnly StartAfter(IEnumerable<DateOnly> predecessorEnds, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(predecessorEnds);

        var ends = predecessorEnds.ToList();
        return ends.Count == 0 ? today : ends.Max().AddDays(1);
    }

    /// <summary>
    /// Every item of <paramref name="items"/> as the projection reads it on
    /// <paramref name="today"/>, in the order given; see <see cref="Lay"/>.
    /// </summary>
    public static IReadOnlyList<RoadmapItemDto> Project(
        IReadOnlyList<RoadmapItemDto> items,
        IReadOnlyDictionary<Guid, RoadmapItemRollupDto> rollups,
        PacesInUseDto paces,
        DateOnly today,
        IReadOnlyList<RoadmapMilestoneDto>? milestones = null) =>
        Lay(items, rollups, paces, today, milestones).Items;

    /// <summary>
    /// Every item of <paramref name="items"/> as the projection reads it on
    /// <paramref name="today"/>, with the parts each was laid out in.
    /// </summary>
    /// <param name="items">The plan's items, in the plan's order — the order the answer
    /// keeps, and the order ties are broken in.</param>
    /// <param name="rollups">What each item gathered, by id; an item missing here gathered
    /// nothing.</param>
    /// <param name="paces">The pace in use per repository and the working week.</param>
    /// <param name="today">The day open work is placed from.</param>
    /// <param name="milestones">The plan's milestones, whose dates hold back the items that
    /// wait on them.</param>
    /// <param name="bandOf">The band a stored repository name lands in; the paces' own
    /// resolution when not given (<see cref="RoadmapItemParts.Of"/>).</param>
    public static RoadmapPlanLayout Lay(
        IReadOnlyList<RoadmapItemDto> items,
        IReadOnlyDictionary<Guid, RoadmapItemRollupDto> rollups,
        PacesInUseDto paces,
        DateOnly today,
        IReadOnlyList<RoadmapMilestoneDto>? milestones = null,
        Func<string, string?>? bandOf = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(rollups);
        ArgumentNullException.ThrowIfNull(paces);

        // Where each node closes, as projected so far: a milestone on its day, an item once
        // it has been laid out.
        var closes = new Dictionary<Guid, DateOnly>();
        foreach (var milestone in milestones ?? []) closes.TryAdd(milestone.Id, milestone.On);

        var projected = new Dictionary<Guid, RoadmapItemDto>();
        var layouts = new Dictionary<Guid, RoadmapItemLayout>();

        foreach (var item in InDependencyOrder(items))
        {
            // A wait on something not laid out yet — gone from the plan, or the item a
            // circle was released at — is no floor.
            var floor = StartAfter(item.DependsOn.Where(closes.ContainsKey).Select(id => closes[id]), today);
            var (read, layout) = One(item, rollups.GetValueOrDefault(item.Id), paces, today, floor, bandOf);

            projected[item.Id] = read;
            layouts[item.Id] = layout;
            closes[item.Id] = read.End;
        }

        return new RoadmapPlanLayout([.. items.Select(item => projected[item.Id])], layouts);
    }

    /// <summary>
    /// One item as the projection reads it, with its parts, from a <paramref name="floor"/>
    /// already worked out — the day after its predecessors end.
    /// </summary>
    public static (RoadmapItemDto Item, RoadmapItemLayout Layout) One(
        RoadmapItemDto item,
        RoadmapItemRollupDto? rollup,
        PacesInUseDto paces,
        DateOnly today,
        DateOnly? floor = null,
        Func<string, string?>? bandOf = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(paces);

        var layout = RoadmapItemParts.Of(item, rollup, paces, today, bandOf, floor);

        if (!KeepsUp(item) || layout.Placement is not (PartsPlacement.ByEffort or PartsPlacement.FromWork)) return (item, layout);
        if (layout.Start == item.Start && layout.End == item.End) return (item, layout);

        return (item with { Start = layout.Start, End = layout.End }, layout);
    }

    /// <summary>Whether the projection may move <paramref name="item"/>'s window: the import
    /// sized it by its effort and nobody has placed it or pinned its end since.</summary>
    public static bool KeepsUp(RoadmapItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return EffortWindow.IsDerived(item) && !item.EndPinned;
    }

    /// <summary>
    /// The items with each after the items it waits on, ties in the order given. A circle —
    /// which the plan refuses, but a reader must not hang on — is broken by releasing the
    /// earliest item still waiting, the way <see cref="RoadmapRollup.InDependencyOrder"/>
    /// breaks one between tasks.
    /// </summary>
    private static List<RoadmapItemDto> InDependencyOrder(IReadOnlyList<RoadmapItemDto> items)
    {
        var ids = items.Select(item => item.Id).ToHashSet();
        var placed = new HashSet<Guid>();
        var waiting = items.DistinctBy(item => item.Id).ToList();
        var ordered = new List<RoadmapItemDto>(waiting.Count);

        while (waiting.Count > 0)
        {
            var next = waiting.FirstOrDefault(item =>
                item.DependsOn.All(id => id == item.Id || !ids.Contains(id) || placed.Contains(id))) ?? waiting[0];

            waiting.Remove(next);
            placed.Add(next.Id);
            ordered.Add(next);
        }

        return ordered;
    }
}

/// <summary>The plan as the projection reads it.</summary>
/// <param name="Items">Every item, in the plan's order, with the window it is read with.</param>
/// <param name="Layouts">Every item's parts, by id — the parts each window was taken from.</param>
public sealed record RoadmapPlanLayout(
    IReadOnlyList<RoadmapItemDto> Items,
    IReadOnlyDictionary<Guid, RoadmapItemLayout> Layouts);
