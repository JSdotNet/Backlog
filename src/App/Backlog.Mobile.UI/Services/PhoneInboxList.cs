using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Outbox;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Services;

/// <summary>The kind chip pressed above the phone Inbox: one at a time, All by default
/// (<c>.devbook/design/interaction-guidelines.md#phone-inbox</c>).</summary>
public enum PhoneInboxFilter
{
    All,
    Captures,
    Notes,
    Meetings
}

/// <summary>What a row of the phone Inbox is: a capture this phone made, or a note
/// or meeting from <c>note_view</c>.</summary>
public enum PhoneInboxKind
{
    Capture,
    Note,
    Meeting
}

/// <summary>One of this phone's captures: what the service has, or a capture still in
/// the outbox — in which case <paramref name="Pending"/> is its entry.</summary>
public sealed record PhoneCaptureRow(InboxItem Item, OutboxEntry? Pending);

/// <summary>One row of the phone Inbox: a capture or a note row, never both.</summary>
public sealed record PhoneInboxRow
{
    private PhoneInboxRow(PhoneCaptureRow? capture, NoteViewRow? note)
    {
        Capture = capture;
        Note = note;
    }

    public static PhoneInboxRow Of(PhoneCaptureRow capture) => new(capture ?? throw new ArgumentNullException(nameof(capture)), null);

    public static PhoneInboxRow Of(NoteViewRow note) => new(null, note ?? throw new ArgumentNullException(nameof(note)));

    public PhoneCaptureRow? Capture { get; }

    public NoteViewRow? Note { get; }

    public Guid Id => Capture?.Item.Id ?? Note!.Id;

    public PhoneInboxKind Kind => Capture is not null
        ? PhoneInboxKind.Capture
        : Note!.IsMeeting ? PhoneInboxKind.Meeting : PhoneInboxKind.Note;

    public string Title => Capture?.Item.Title ?? Note!.Title;

    /// <summary>The Markdown body; empty for one with none.</summary>
    public string Body => Capture is { } capture ? capture.Item.BodyMd ?? string.Empty : Note!.Body;

    /// <summary>What the list is ordered by: when a capture was made, when a note last changed.</summary>
    public DateTimeOffset At => Capture?.Item.CapturedAt ?? Note!.UpdatedAt;
}

/// <summary>
/// The phone Inbox's one list (<c>.devbook/domain/inbox/features.md#notes-on-the-phone</c>):
/// the captures this phone made that the desktop has not taken in, beside every note and
/// meeting the phone holds, newest first, narrowed by the kind chip pressed and by the
/// words searched for. The phone keeps no rule beyond that; the rows are the callers'.
/// </summary>
public static class PhoneInboxList
{
    public static IReadOnlyList<PhoneInboxRow> Of(
        IEnumerable<PhoneCaptureRow> captures,
        IEnumerable<NoteViewRow> notes,
        PhoneInboxFilter filter,
        string? query = null)
    {
        ArgumentNullException.ThrowIfNull(captures);
        ArgumentNullException.ThrowIfNull(notes);

        var words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return
        [
            .. captures.Select(PhoneInboxRow.Of)
                .Concat(notes.Select(PhoneInboxRow.Of))
                .Where(row => Shows(filter, row.Kind))
                .Where(row => words.All(word =>
                    row.Title.Contains(word, StringComparison.CurrentCultureIgnoreCase)
                    || row.Body.Contains(word, StringComparison.CurrentCultureIgnoreCase)))
                .OrderByDescending(row => row.At)
        ];
    }

    private static bool Shows(PhoneInboxFilter filter, PhoneInboxKind kind) => filter switch
    {
        PhoneInboxFilter.Captures => kind is PhoneInboxKind.Capture,
        PhoneInboxFilter.Notes => kind is PhoneInboxKind.Note,
        PhoneInboxFilter.Meetings => kind is PhoneInboxKind.Meeting,
        _ => true
    };
}
