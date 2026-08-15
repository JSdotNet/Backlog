using Backlog.Desktop.UI.Inbox;
using Backlog.Modules.Backlog.Abstractions;

namespace Backlog.Desktop.UI.BacklogManagement;

/// <summary>
/// The backlog entries nobody has decided on yet, offered to the Inbox in the
/// Inbox's own words.
/// <para>
/// <strong>This is scaffolding.</strong> Inbox is a Core context in
/// <c>.domain/context-map.md</c>, with its own capture sources and a triage
/// decision — and it owns none of that yet. Until it does, the Inbox pane shows
/// backlog drafts. When capture becomes real the pane keeps working and this
/// file is what gets deleted.
/// </para>
/// <para>
/// It lives here rather than in <c>Inbox/</c> on purpose: it needs
/// <see cref="EntryRow"/>, and an Inbox that needs the backlog to render is an
/// Inbox that can never be lifted out. Backlog Management conforms to the Inbox's
/// published <see cref="InboxItem"/> contract instead — the direction the context
/// map already has between the two, and the one cross-context reference
/// <c>DesktopDomainBoundaryTests</c> allows.
/// </para>
/// </summary>
public static class BacklogDrafts
{
    /// <summary>How many drafts the pane shows before it stops being a triage
    /// queue and starts being a second backlog.</summary>
    private const int MaxItems = 12;

    /// <summary>The untriaged drafts, as inbox items.</summary>
    public static IReadOnlyList<InboxItem> ForInbox(IEnumerable<EntryRow> rows) =>
        [.. rows
            .Where(row => !row.IsUntouched && row.PreviewStatus == EntryStatus.Draft)
            .OrderBy(row => row.PreviewTitle, StringComparer.OrdinalIgnoreCase)
            .Take(MaxItems)
            .Select(row => new InboxItem(row.Key.ToString(), row.PreviewTitle, row.PreviewArea))];

    /// <summary>The row an item came from, or null when it has since gone.</summary>
    public static EntryRow? Find(IEnumerable<EntryRow> rows, InboxItem item) =>
        rows.FirstOrDefault(row => string.Equals(row.Key.ToString(), item.Key, StringComparison.Ordinal));
}
