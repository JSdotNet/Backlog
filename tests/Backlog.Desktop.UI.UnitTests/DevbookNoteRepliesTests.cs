namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// How a review note's replies live in its remark's body: folded below the note
/// by the one-time import and by <c>devbook_post_note</c>, read back out as
/// <c>{ by, at, body }</c> when the note is answered over
/// <c>devbook.decision@1</c>.
/// </summary>
public sealed class DevbookNoteRepliesTests
{
    [Fact]
    public void Folded_replies_read_back_as_the_note_and_its_replies()
    {
        var once = DevbookNoteReplies.Fold("Is the fee ours?", "DEV-TOWER", "2026-10-30T09:15:00Z", "Yes.");
        var twice = DevbookNoteReplies.Fold(once, "claude/flow-spec", "2026-10-31", "Confirmed.\nBy finance.");

        Assert.Equal("Is the fee ours?\n\n— DEV-TOWER, 2026-10-30T09:15:00Z: Yes.", once);

        var (body, replies) = DevbookNoteReplies.Split(twice);

        Assert.Equal("Is the fee ours?", body);
        Assert.Equal(
            [
                new DevbookNoteReply("DEV-TOWER", "2026-10-30T09:15:00Z", "Yes."),
                new DevbookNoteReply("claude/flow-spec", "2026-10-31", "Confirmed.\nBy finance.")
            ],
            replies);
    }

    /// <summary>A reply whose own text holds what looks like another
    /// <c>, date: </c> splits at the first one, after the author — never inside
    /// what was said.</summary>
    [Fact]
    public void A_reply_whose_text_holds_a_date_and_colon_keeps_its_text()
    {
        const string Said = "Moved to the release on 3 December, 2026-12-01: the freeze.";

        var (body, replies) = DevbookNoteReplies.Split(DevbookNoteReplies.Fold("When?", "DEV-TOWER", "2026-10-30T09:15:00Z", Said));

        Assert.Equal("When?", body);
        Assert.Equal(new DevbookNoteReply("DEV-TOWER", "2026-10-30T09:15:00Z", Said), Assert.Single(replies));
    }

    /// <summary>The import's shape — a fence reply's author and its date as
    /// written — splits like any other; an author may hold a comma.</summary>
    [Fact]
    public void An_import_shaped_reply_splits()
    {
        var (body, replies) = DevbookNoteReplies.Split(
            "Is the hour measured from payment\nor from placing the order?\n\n— claude/flow-spec, 2026-09-04: From payment.\n— Schepers, Job, 2026-09-05: Agreed.");

        Assert.Equal("Is the hour measured from payment\nor from placing the order?", body);
        Assert.Equal(
            [
                new DevbookNoteReply("claude/flow-spec", "2026-09-04", "From payment."),
                new DevbookNoteReply("Schepers, Job", "2026-09-05", "Agreed.")
            ],
            replies);
    }

    /// <summary>An author's own dash lines are the note: an attribution, a dash
    /// paragraph in the middle, a line with a date that is no ISO date, or a
    /// line that names no one.</summary>
    [Theory]
    [InlineData("Make it work, make it right, make it fast.\n\n— Kent Beck")]
    [InlineData("A thought.\n\n— an aside the author wrote\n\nand more of the note.")]
    [InlineData("Quoted.\n\n— Kent Beck, 1999: Extreme Programming Explained")]
    [InlineData("Quoted.\n\n— 2026-09-04: no one named")]
    [InlineData("— starts with a dash")]
    [InlineData("Plain.")]
    public void A_body_with_no_folded_block_is_all_note(string stored)
    {
        var (body, replies) = DevbookNoteReplies.Split(stored);

        Assert.Equal(stored, body);
        Assert.Empty(replies);
    }

    /// <summary>A fold <see cref="DevbookNoteReplies.Split"/> could not read back
    /// is refused rather than written.</summary>
    [Theory]
    [InlineData(" ", "2026-09-04")]
    [InlineData("Job: the reviewer", "2026-09-04")]
    [InlineData("Job", "yesterday")]
    [InlineData("Job", "")]
    public void A_fold_that_would_not_read_back_is_refused(string by, string at) =>
        Assert.ThrowsAny<ArgumentException>(() => DevbookNoteReplies.Fold("Why?", by, at, "Because."));

    [Fact]
    public void A_stored_body_with_windows_line_endings_reads_the_same() =>
        Assert.Equal(
            new DevbookNoteReply("Job", "2026-09-04", "Agreed."),
            Assert.Single(DevbookNoteReplies.Split("Why?\r\n\r\n— Job, 2026-09-04: Agreed.\r\n").Replies));
}
