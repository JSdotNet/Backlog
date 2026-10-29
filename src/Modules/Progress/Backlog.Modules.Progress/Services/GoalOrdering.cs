using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.DomainModels;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// Puts a goal at a place in the person's order (<see cref="GoalOrder"/>). A goal travels as
/// one document, decided last-write-wins on its own (local ADR 0035 §9), so a move writes the
/// one goal that moved: it takes a rank halfway between its new neighbours' keys, or a
/// <see cref="GoalOrder.Gap"/> beyond the first or the last. Only when the neighbours have no
/// room left between them are the goals around the move spread out again — the window
/// widening until it holds room — and those are written too.
/// </summary>
public static class GoalOrdering
{
    /// <summary>
    /// Moves <paramref name="goal"/> to <paramref name="toIndex"/> among
    /// <paramref name="all"/> in the person's order, clamped to the list. Answers the goals
    /// whose rank changed, <paramref name="goal"/> among them, to be saved; none when the goal
    /// already holds that place.
    /// </summary>
    public static IReadOnlyList<Goal> MoveTo(IReadOnlyList<Goal> all, Goal goal, int toIndex)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(goal);

        var ordered = GoalOrder.Sort(all, g => g.Rank, g => g.CreatedAt, g => g.Id).ToList();
        var from = ordered.FindIndex(g => g.Id == goal.Id);
        if (from < 0) return [];

        var to = Math.Clamp(toIndex, 0, ordered.Count - 1);
        if (from == to) return [];

        ordered.RemoveAt(from);
        ordered.Insert(to, goal);

        // A window of goals around the move gets keys spread evenly between the keys just
        // outside it. It starts as the moved goal alone — the midpoint, or a gap beyond an
        // end — and widens only when there is no room.
        for (var radius = 0; ; radius = radius == 0 ? 1 : radius * 2)
        {
            var start = Math.Max(0, to - radius);
            var end = Math.Min(ordered.Count - 1, to + radius);

            if (Spread(ordered, start, end) is not { } ranks) continue;

            var written = new List<Goal>();
            for (var index = start; index <= end; index++)
            {
                var placed = ordered[index];
                var rank = ranks[index - start];
                if (placed.Id != goal.Id && placed.Rank == rank) continue;

                placed.PlaceAt(rank);
                written.Add(placed);
            }

            return written;
        }
    }

    /// <summary>
    /// Makes sure the new goal <paramref name="goal"/> lands last among <paramref name="all"/>.
    /// Set now, it is keyed by the newest moment and is last already — unless a goal moved to
    /// the bottom, or spread there by a re-rank, holds a rank a gap past the newest key and so
    /// ahead of the clock. Then the new goal is ranked a gap past that one: written with its
    /// own create, so placing it is no second write. Answers whether it was ranked.
    /// </summary>
    public static bool PlaceNew(IReadOnlyList<Goal> all, Goal goal)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(goal);

        var keys = all.Where(other => other.Id != goal.Id).Select(other => other.OrderKey).Where(key => key != long.MaxValue).ToList();
        if (keys.Count == 0) return false;

        var last = keys.Max();
        if (goal.OrderKey > last) return false;

        goal.PlaceAt((long)Int128.Min((Int128)last + GoalOrder.Gap, long.MaxValue - 1));
        return true;
    }

    /// <summary>Keys for the goals from <paramref name="start"/> to <paramref name="end"/>,
    /// strictly rising and strictly between the keys outside the window; null when they do
    /// not fit.</summary>
    private static long[]? Spread(List<Goal> ordered, int start, int end)
    {
        var count = end - start + 1;
        Int128? lower = start > 0 ? ordered[start - 1].OrderKey : null;
        Int128? upper = end < ordered.Count - 1 ? ordered[end + 1].OrderKey : null;

        Int128 first, step;
        switch (lower, upper)
        {
            case (null, null):
                first = GoalOrder.Gap;
                step = GoalOrder.Gap;
                break;
            case (null, { } high):
                first = high - (Int128)count * GoalOrder.Gap;
                step = GoalOrder.Gap;
                break;
            case ({ } low, null):
                first = low + GoalOrder.Gap;
                step = GoalOrder.Gap;
                break;
            case ({ } low, { } high):
                step = (high - low) / (count + 1);
                if (step < 1) return null;
                first = low + step;
                break;
        }

        var last = first + (Int128)(count - 1) * step;
        if (first < long.MinValue || last >= long.MaxValue) return null;

        var ranks = new long[count];
        for (var index = 0; index < count; index++) ranks[index] = (long)(first + (Int128)index * step);
        return ranks;
    }
}
