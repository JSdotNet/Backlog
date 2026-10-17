using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Progress;

/// <summary>
/// One Progress document as the phone last heard of it — a goal, an impediment or the
/// stand-up context — kept whole as the desktop wrote it, with the stamps it is ordered by.
/// The phone reads these and owns none of them (local ADR 0035 §8).
/// </summary>
/// <param name="Kind">The document's kind token: <see cref="ProgressFold.GoalType"/>,
/// <see cref="ProgressFold.ImpedimentType"/> or <see cref="ProgressFold.StandUpContextType"/>.</param>
/// <param name="UpdatedAt">The document's own last-write-wins stamp.</param>
/// <param name="DeletedAt">Set on a tombstone, which is kept hidden so an older copy
/// arriving later cannot bring the document back.</param>
/// <param name="ServerTimestamp">The replica's ordering stamp, which breaks a tie between
/// two copies with the same <paramref name="UpdatedAt"/>.</param>
/// <param name="Content">The document's text, verbatim.</param>
public sealed record ProgressViewRow(
    Guid Id,
    string Kind,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt,
    long ServerTimestamp,
    string Content);

/// <summary>
/// The task feed's goal, impediment and stand-up context documents, folded into one row per
/// document under the rule <see cref="Tasks.TaskFold"/> keeps for tasks: the later
/// <see cref="TaskChange.UpdatedAt"/> wins, an equal stamp goes to the higher
/// <see cref="TaskChangeRecord.ServerTimestamp"/> and then to the tombstone, and a tombstone
/// is kept as a hidden row. Order-free, so a page arriving twice or late folds to the same
/// rows. Every other kind is left to the folds that keep it.
/// </summary>
public static class ProgressFold
{
    /// <summary>The kind token of a goal. A literal duplicated from the desktop's sync
    /// client, for the reason <see cref="Tasks.TaskFold.CaptureType"/> is one; persisted on
    /// every replica, so never renamed.</summary>
    public const string GoalType = Tasks.TaskFold.GoalType;

    /// <summary>The kind token of an impediment, on the terms <see cref="GoalType"/> is.</summary>
    public const string ImpedimentType = Tasks.TaskFold.ImpedimentType;

    /// <summary>The kind token of the stand-up context a desktop writes for the phone.</summary>
    public const string StandUpContextType = Tasks.TaskFold.StandUpContextType;

    /// <summary>The kind token of an impediment edit the phone queues.</summary>
    public const string ImpedimentEditType = Tasks.TaskFold.ImpedimentEditType;

    /// <summary>Whether this fold keeps documents of <paramref name="type"/>. Ordinal, as
    /// every token on the feed is compared.</summary>
    public static bool Keeps(string? type) =>
        string.Equals(type, GoalType, StringComparison.Ordinal)
        || string.Equals(type, ImpedimentType, StringComparison.Ordinal)
        || string.Equals(type, StandUpContextType, StringComparison.Ordinal);

    /// <summary>Folds the Progress documents among <paramref name="records"/> into
    /// <paramref name="rows"/>, and answers with the rows that changed — the ones to write.</summary>
    public static IReadOnlyList<ProgressViewRow> Apply(IDictionary<Guid, ProgressViewRow> rows, IEnumerable<TaskChangeRecord> records)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(records);

        var changed = new Dictionary<Guid, ProgressViewRow>();

        foreach (var record in records)
        {
            var change = record.Change;
            if (!Keeps(change.Task.Type)) continue;

            var incoming = new ProgressViewRow(
                change.Id,
                change.Task.Type,
                change.UpdatedAt,
                change.DeletedAt,
                record.ServerTimestamp,
                change.Task.ContentMd ?? string.Empty);

            if (rows.TryGetValue(change.Id, out var current) && !Wins(incoming, current)) continue;

            rows[change.Id] = incoming;
            changed[change.Id] = incoming;
        }

        return [.. changed.Values];
    }

    /// <summary>Whether <paramref name="incoming"/> replaces <paramref name="current"/>: a
    /// strict order over every field that can differ, so two phones folding the same records
    /// in different orders end on the same row.</summary>
    public static bool Wins(ProgressViewRow incoming, ProgressViewRow current)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(current);

        if (incoming.UpdatedAt != current.UpdatedAt) return incoming.UpdatedAt > current.UpdatedAt;
        if (incoming.ServerTimestamp != current.ServerTimestamp) return incoming.ServerTimestamp > current.ServerTimestamp;

        return incoming.DeletedAt is not null && current.DeletedAt is null;
    }
}
