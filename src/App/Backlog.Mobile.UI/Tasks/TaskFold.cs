using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Tasks;

/// <summary>
/// The task feed, folded into one row per task. No domain rules: which copy of a
/// task wins is decided by its stamps and nothing else, the same whole-document
/// last-write-wins the replica keeps (.devbook/arc42/adr/0005).
/// <para>
/// The fold is order-free, which is what lets a page arrive twice, or late: the
/// later <see cref="TaskChange.UpdatedAt"/> wins, an equal stamp goes to the
/// higher <see cref="TaskChangeRecord.ServerTimestamp"/> and then to the tombstone,
/// and a deletion is kept as a hidden row rather than an absence — an absence
/// would let the older copy behind it back in.
/// </para>
/// </summary>
public static class TaskFold
{
    /// <summary>The type token a capture document carries. A capture is the
    /// Inbox's, not a task (.devbook/arc42/adr/0009), and the Tasks tab never
    /// shows one.</summary>
    public const string CaptureType = "capture";

    /// <summary>Folds <paramref name="records"/> into <paramref name="rows"/>, and
    /// answers with the rows that changed — the ones to write.</summary>
    public static IReadOnlyList<TaskViewRow> Apply(IDictionary<Guid, TaskViewRow> rows, IEnumerable<TaskChangeRecord> records)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(records);

        var changed = new Dictionary<Guid, TaskViewRow>();

        foreach (var record in records)
        {
            var change = record.Change;
            if (string.Equals(change.Task.Type, CaptureType, StringComparison.OrdinalIgnoreCase)) continue;

            var incoming = new TaskViewRow(change.Id, change.UpdatedAt, change.DeletedAt, record.ServerTimestamp, change.Task);

            if (rows.TryGetValue(change.Id, out var current) && !Wins(incoming, current)) continue;

            rows[change.Id] = incoming;
            changed[change.Id] = incoming;
        }

        return [.. changed.Values];
    }

    /// <summary>Whether <paramref name="incoming"/> replaces <paramref name="current"/>.
    /// A strict order over every field that can differ, so two phones folding the
    /// same records in different orders end on the same row.</summary>
    public static bool Wins(TaskViewRow incoming, TaskViewRow current)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(current);

        if (incoming.UpdatedAt != current.UpdatedAt) return incoming.UpdatedAt > current.UpdatedAt;
        if (incoming.ServerTimestamp != current.ServerTimestamp) return incoming.ServerTimestamp > current.ServerTimestamp;

        return incoming.DeletedAt is not null && current.DeletedAt is null;
    }
}
