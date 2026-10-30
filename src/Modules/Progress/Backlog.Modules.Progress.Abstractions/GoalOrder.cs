namespace Backlog.Modules.Progress.Abstractions;

/// <summary>
/// The order goals are listed in, the person's own — one rule for every reader: the
/// desktop's store, the module, and the phone, which reads the <c>goal</c> documents without
/// the module behind it.
/// <para>
/// A goal's <b>order key</b> is its rank when the person placed it by hand, and otherwise the
/// moment it was set, in UTC ticks: goals nobody moved keep the order they were set in, and a
/// new goal, holding the newest moment, lands last — ranked past the last key when a goal
/// moved to the bottom holds one ahead of the clock. A rank lives on the same scale, so placing
/// one goal between two never-moved ones writes only the goal that moved. A goal that says
/// neither sorts last. The id breaks every tie — two devices that placed two goals on one rank
/// still list them alike.
/// </para>
/// </summary>
public static class GoalOrder
{
    /// <summary>How far beyond the first or the last goal a goal moved to an end is placed:
    /// an hour of ticks, so many moves to the same end stay far apart.</summary>
    public const long Gap = TimeSpan.TicksPerHour;

    /// <summary>The order key of a goal with <paramref name="rank"/>, set at
    /// <paramref name="createdAt"/>.</summary>
    public static long Key(long? rank, DateTimeOffset? createdAt) =>
        rank ?? createdAt?.UtcTicks ?? long.MaxValue;

    /// <summary><paramref name="items"/> in the person's order: by <see cref="Key"/>, then by id.</summary>
    public static IOrderedEnumerable<T> Sort<T>(
        IEnumerable<T> items,
        Func<T, long?> rank,
        Func<T, DateTimeOffset?> createdAt,
        Func<T, Guid> id)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(rank);
        ArgumentNullException.ThrowIfNull(createdAt);
        ArgumentNullException.ThrowIfNull(id);

        return items.OrderBy(item => Key(rank(item), createdAt(item))).ThenBy(id);
    }
}
