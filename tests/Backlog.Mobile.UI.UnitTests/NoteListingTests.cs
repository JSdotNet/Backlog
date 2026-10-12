using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Outbox;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// What a row on the Notes tab says about a note: when it last changed, in the
/// fewest words that still place it, and the opening of its body as plain text.
/// </summary>
public sealed class NoteListingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 14, 30, 0, TimeSpan.FromHours(2));

    [Theory]
    [InlineData(0, 9, 50, "09:50")]
    [InlineData(-1, 23, 59, "Yesterday")]
    [InlineData(-2, 8, 0, "Mon")]
    [InlineData(-6, 8, 0, "Thu")]
    [InlineData(-7, 8, 0, "30 Sep")]
    [InlineData(-300, 8, 0, "11 Dec 2025")]
    public void A_time_is_the_clock_today_the_weekday_this_week_and_the_date_before_that(int days, int hour, int minute, string expected)
    {
        var day = Now.Date.AddDays(days);
        var at = new DateTimeOffset(day.Year, day.Month, day.Day, hour, minute, 0, Now.Offset);

        Assert.Equal(expected, NoteTimeLabel.For(at, Now));
    }

    [Fact]
    public void A_note_stamped_ahead_of_the_phones_clock_still_reads_as_today()
    {
        Assert.Equal("14:35", NoteTimeLabel.For(Now.AddMinutes(5), Now));
    }

    [Fact]
    public void A_snippet_is_the_body_as_prose_without_its_markdown()
    {
        const string body = """
            # Conflict cases

            Agreed with **Anna** on how two devices settle an edit:

            - [ ] Last write wins for the _title_.
            1. See [the spec](https://example.test/spec) and `NoteFold`.
            > A delete beats an edit.
            ```
            code is not prose
            ```
            """;

        Assert.Equal(
            "Conflict cases Agreed with Anna on how two devices settle an edit: Last write wins for the title. See the spec and NoteFold. A delete beats an edit.",
            NoteSnippet.Of(body));
    }

    [Fact]
    public void A_title_made_from_the_first_line_is_not_repeated_in_the_snippet()
    {
        Assert.Equal("Shipped sync.", NoteSnippet.Of("Standup\nShipped sync.", "Standup"));
        Assert.Equal(string.Empty, NoteSnippet.Of("Offsite: by train.", "Offsite: by train."));
        Assert.Equal("Shipped sync.", NoteSnippet.Of("Shipped sync.", "Standup"));
    }

    [Fact]
    public void An_empty_body_has_no_snippet_and_a_long_one_is_cut_at_a_word()
    {
        Assert.Equal(string.Empty, NoteSnippet.Of("   \n "));

        var snippet = NoteSnippet.Of(string.Join(' ', Enumerable.Repeat("word", 100)));

        Assert.True(snippet.Length <= NoteSnippet.MaximumLength + 1, snippet);
        Assert.EndsWith("word…", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_outbox_holds_a_note_until_the_entry_carrying_it_is_delivered()
    {
        var store = new InMemoryDeviceStore();
        using var outbox = new DeviceOutbox(store, [], TimeProvider.System);
        var noteId = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var change = NoteViewProjection.NewNote(noteId, "Standup", "", [], DateTimeOffset.UtcNow);

        Assert.False(NoteOutboxKind.Holds(outbox, noteId));

        await outbox.EnqueueAsync(NoteOutboxKind.Token, Guid.CreateVersion7(), NoteOutboxKind.Write(new NoteOutboxPayload(change, [], [])), TestContext.Current.CancellationToken);

        Assert.True(NoteOutboxKind.Holds(outbox, noteId));
        Assert.False(NoteOutboxKind.Holds(outbox, other));
    }
}
