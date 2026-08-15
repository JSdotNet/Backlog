using Backlog.Modules.Backlog.Abstractions;
using Backlog.Modules.Backlog.DomainModels;

namespace Backlog.Modules.Backlog.Services;

/// <summary>
/// The half of the entry text format that touches the aggregate.
/// <para>
/// Reading and writing the text itself is a published language and lives in
/// Abstractions, where an editor can use it. Turning parsed text into sub-item
/// mutations is not: it changes an aggregate, so it stays behind the module
/// boundary where the invariants are.
/// </para>
/// </summary>
internal static class EntryTextSync
{
    /// <summary>Syncs parsed sub-items onto the entry's structured sub-items by
    /// position — the typed text is the single source of truth; nothing outside
    /// this entry references a sub-item's id, so re-deriving identity from
    /// position on every save is safe.</summary>
    public static void SyncSubItems(BacklogEntry entry, IReadOnlyList<EntryTextParser.ParsedSubItem> parsedItems)
    {
        var existing = entry.SubItems.OrderBy(s => s.Order).ToList();

        for (var idx = existing.Count - 1; idx >= parsedItems.Count; idx--)
        {
            entry.RemoveSubItem(existing[idx].Id);
        }

        existing = entry.SubItems.OrderBy(s => s.Order).ToList();

        for (var idx = 0; idx < parsedItems.Count; idx++)
        {
            var parsed = parsedItems[idx];
            var wantStatus = parsed.Done ? SubItemStatus.Done : SubItemStatus.Pending;

            if (idx < existing.Count)
            {
                var item = existing[idx];
                if (!string.Equals(item.Title, parsed.Title, StringComparison.Ordinal)
                    || !string.Equals(item.Notes, parsed.Notes, StringComparison.Ordinal))
                {
                    entry.UpdateSubItem(item.Id, parsed.Title, parsed.Notes);
                }

                if (item.Status != wantStatus)
                {
                    entry.SetSubItemStatus(item.Id, wantStatus);
                }
            }
            else
            {
                var newItem = entry.AddSubItem(parsed.Title, parsed.Notes);
                if (parsed.Done)
                {
                    entry.SetSubItemStatus(newItem.Id, SubItemStatus.Done);
                }
            }
        }
    }
}
