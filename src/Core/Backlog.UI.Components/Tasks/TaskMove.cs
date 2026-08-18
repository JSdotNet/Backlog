namespace Backlog.UI.Components.Tasks;

/// <summary>
/// A row was moved onto another row. Two ids and nothing else: the list knows
/// what the reader did, and the host knows what the order means and where it is
/// kept, so applying the move belongs there.
/// </summary>
/// <param name="Id">The row that was picked up.</param>
/// <param name="TargetId">The row it was dropped on. The moved row takes this
/// one's place, which is what a reader expects from dropping a thing on another
/// thing — never "after" or "before", because at the ends of a list those two
/// stop meaning the same as what they saw.</param>
public sealed record TaskMove(string Id, string TargetId)
{
    /// <summary>
    /// The list with the move applied, for a host that has no opinion of its own
    /// about ordering.
    /// <para>
    /// Offered rather than done, because the common case is a host that stores
    /// its own order and has to write it somewhere; a component that reordered
    /// the list it was handed would be a component whose result the host has to
    /// undo before saving what it actually keeps.
    /// </para>
    /// </summary>
    public IReadOnlyList<T> ApplyTo<T>(IReadOnlyList<T> items, Func<T, string> id)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(id);

        var ordered = items.ToList();
        var from = ordered.FindIndex(item => string.Equals(id(item), Id, StringComparison.Ordinal));
        var to = ordered.FindIndex(item => string.Equals(id(item), TargetId, StringComparison.Ordinal));

        // Either id naming nothing means the list moved under the drag. Doing
        // nothing is the honest outcome; guessing a position is not.
        if (from < 0 || to < 0 || from == to) return items;

        var moved = ordered[from];
        ordered.RemoveAt(from);
        ordered.Insert(to, moved);

        return ordered;
    }
}
