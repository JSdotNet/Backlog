using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Notes;

/// <summary>
/// The task feed's note documents, folded into one row per note
/// (<c>.devbook/arc42/06-runtime-view.md#mobile-note-sync</c>). The fold
/// <see cref="Tasks.TaskFold"/> keeps for tasks, over the one kind it skips: the
/// later <see cref="TaskChange.UpdatedAt"/> wins, an equal stamp goes to the
/// higher <see cref="TaskChangeRecord.ServerTimestamp"/> and then to the
/// tombstone, and a tombstone is kept as a hidden row rather than an absence.
/// Order-free, so a page arriving twice or late folds to the same rows.
/// </summary>
public static class NoteFold
{
    /// <summary>The kind token a note document carries. A literal duplicated from
    /// the desktop's sync client, for the reason <see cref="Tasks.TaskFold.CaptureType"/>
    /// is one: neither side may see the other.</summary>
    public const string NoteType = "note";

    /// <summary>Whether a document is a note's. Ordinal, as every token on the
    /// feed is compared.</summary>
    public static bool IsNote(string? type) => string.Equals(type, NoteType, StringComparison.Ordinal);

    /// <summary>Folds the note documents among <paramref name="records"/> into
    /// <paramref name="rows"/>, and answers with the rows that changed — the ones
    /// to write. Every other kind is left to the task fold.</summary>
    public static IReadOnlyList<NoteViewRow> Apply(IDictionary<Guid, NoteViewRow> rows, IEnumerable<TaskChangeRecord> records)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(records);

        var changed = new Dictionary<Guid, NoteViewRow>();

        foreach (var record in records)
        {
            var change = record.Change;
            if (!IsNote(change.Task.Type)) continue;

            var incoming = new NoteViewRow(change.Id, change.UpdatedAt, change.DeletedAt, record.ServerTimestamp, change.Task);

            if (rows.TryGetValue(change.Id, out var current) && !Wins(incoming, current)) continue;

            rows[change.Id] = incoming;
            changed[change.Id] = incoming;
        }

        return [.. changed.Values];
    }

    /// <summary>Whether <paramref name="incoming"/> replaces <paramref name="current"/>:
    /// a strict order over every field that can differ, so two phones folding the
    /// same records in different orders end on the same row.</summary>
    public static bool Wins(NoteViewRow incoming, NoteViewRow current)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(current);

        if (incoming.UpdatedAt != current.UpdatedAt) return incoming.UpdatedAt > current.UpdatedAt;
        if (incoming.ServerTimestamp != current.ServerTimestamp) return incoming.ServerTimestamp > current.ServerTimestamp;

        return incoming.DeletedAt is not null && current.DeletedAt is null;
    }
}
