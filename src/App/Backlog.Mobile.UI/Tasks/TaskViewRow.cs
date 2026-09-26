using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Tasks;

/// <summary>
/// One task as the phone last heard of it: the replica's document, the stamps it
/// is ordered by, and nothing the phone worked out for itself.
/// </summary>
/// <param name="UpdatedAt">The document's own last-write-wins stamp.</param>
/// <param name="DeletedAt">Set on a tombstone. A deleted task keeps its row, hidden,
/// so an older copy arriving in a later page cannot bring it back.</param>
/// <param name="ServerTimestamp">The replica's ordering stamp, which breaks a tie
/// between two copies with the same <paramref name="UpdatedAt"/>. Zero on a task
/// this phone wrote itself and has not pulled back yet, so the replica's own copy
/// of the same write always replaces it.</param>
public sealed record TaskViewRow(
    Guid Id,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt,
    long ServerTimestamp,
    TaskPayload Task)
{
    /// <summary>Finished with, one way or the other: My Day never lists these.</summary>
    public bool IsClosed =>
        string.Equals(Task.Status, "done", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Task.Status, "archived", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// In My Day on <paramref name="today"/>: picked for exactly that date, not
    /// deleted and not closed. A pick carries the date it was made for and lapses
    /// by arithmetic, so yesterday's is simply not today's — and a due date plays
    /// no part, because My Day is this morning's choice rather than a deadline.
    /// </summary>
    public bool IsInMyDay(DateOnly today) =>
        DeletedAt is null && !IsClosed && Task.InMyDayOn == today;
}
