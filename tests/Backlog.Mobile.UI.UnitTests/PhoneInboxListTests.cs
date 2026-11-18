using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone Inbox's one list (.devbook/domain/inbox/features.md#notes-on-the-phone):
/// the captures this phone made beside every note and meeting, newest first, each
/// with its kind, narrowed by one kind chip and by the words searched for.
/// </summary>
public sealed class PhoneInboxListTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly PhoneCaptureRow Capture =
        new(new InboxItem(Guid.CreateVersion7(), "Buy filters", "mobile", Noon.AddMinutes(-30)), Pending: null);

    private static readonly NoteViewRow Note = Row("Release plan", "ship on Friday", Noon.AddMinutes(-10), noteKind: null);

    private static readonly NoteViewRow Meeting = Row("Weekly sync", "agreed the scope", Noon, noteKind: "meeting");

    [Fact]
    public void All_lists_captures_notes_and_meetings_together_newest_first_each_with_its_kind()
    {
        var rows = PhoneInboxList.Of([Capture], [Note, Meeting], PhoneInboxFilter.All);

        Assert.Equal(["Weekly sync", "Release plan", "Buy filters"], rows.Select(row => row.Title));
        Assert.Equal([PhoneInboxKind.Meeting, PhoneInboxKind.Note, PhoneInboxKind.Capture], rows.Select(row => row.Kind));
    }

    [Theory]
    [InlineData(PhoneInboxFilter.Captures, "Buy filters")]
    [InlineData(PhoneInboxFilter.Notes, "Release plan")]
    [InlineData(PhoneInboxFilter.Meetings, "Weekly sync")]
    public void A_kind_chip_shows_only_the_items_of_its_kind(PhoneInboxFilter filter, string only)
    {
        var rows = PhoneInboxList.Of([Capture], [Note, Meeting], filter);

        Assert.Equal(only, Assert.Single(rows).Title);
    }

    [Fact]
    public void A_search_keeps_the_rows_that_hold_every_word_in_their_title_or_body()
    {
        var rows = PhoneInboxList.Of([Capture], [Note, Meeting], PhoneInboxFilter.All, "friday SHIP");

        Assert.Equal("Release plan", Assert.Single(rows).Title);
    }

    private static NoteViewRow Row(string title, string body, DateTimeOffset at, string? noteKind) =>
        new(Guid.CreateVersion7(), at, DeletedAt: null, ServerTimestamp: 1, new TaskPayload(
            title, body, "note", "draft", "medium", 0, null, at, "mobile", null, null, null, null, null, null, null, null, null, null,
            [], [], [], [], [], [], NoteKind: noteKind));
}
