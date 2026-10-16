using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.Abstractions;

/// <summary>
/// Whether a roadmap item's work is over, read from the two places that can say so: the
/// tasks it gathered, and — for an item that gathered none — the day a person completed it
/// by hand (<see cref="RoadmapItemDto.CompletedOn"/>).
/// <para>
/// Tasks win. While an item gathers at least one backlog entry, the hand-set day is not
/// read: the item is finished when every entry is done, and ends when the last was ticked
/// off. Knowledge chapters are no tasks — a chapter has no status — so an item made only
/// of references is still one a person completes.
/// </para>
/// <para>
/// One rule for every reader — the layout, the keep-up projection, the roadmap's bars and
/// the dashboard's outlook — and here rather than on the rollup, because the rollup is
/// Tasks' answer and the hand-set day is the plan's own.
/// </para>
/// </summary>
public static class RoadmapItemCompletion
{
    /// <summary>Whether tasks carry the item: it gathered at least one backlog entry.
    /// Work that was not read gathered nothing.</summary>
    public static bool HasTasks(RoadmapItemRollupDto? rollup) => rollup is { BacklogEntries.Count: > 0 };

    /// <summary>Whether the item is finished because a person said so: it carries a
    /// completion day and no task outranks it.</summary>
    public static bool IsCompletedByHand(RoadmapItemDto item, RoadmapItemRollupDto? rollup)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.CompletedOn is not null && !HasTasks(rollup);
    }

    /// <summary>Whether the item's work is over: every gathered task is done, or, when it
    /// gathered none, a person completed it.</summary>
    public static bool IsFinished(RoadmapItemDto item, RoadmapItemRollupDto? rollup)
    {
        ArgumentNullException.ThrowIfNull(item);
        return HasTasks(rollup) ? rollup!.IsFinished : item.CompletedOn is not null;
    }

    /// <summary>When the work ended: the day the last gathered task was ticked off, or the
    /// day a person completed a task-less item. Null while neither says.</summary>
    public static DateOnly? FinishedOn(RoadmapItemDto item, RoadmapItemRollupDto? rollup)
    {
        ArgumentNullException.ThrowIfNull(item);
        return HasTasks(rollup) ? rollup!.LastCompletedOn : item.CompletedOn;
    }
}
