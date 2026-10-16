using Backlog.Infrastructure.Devbook.Building;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The text of a note written into the Brain — by the pane's Capture, by Inbox
/// triage's To Brain, and by the capture tool after them
/// (<c>.devbook/domain/devbook/requirements.md#capture-into-the-brain</c>). Read
/// back through the builder's own parse, because "never becomes structure" is a
/// claim about what that parse finds.
/// </summary>
public sealed class BrainNoteTextTests
{
    [Fact]
    public void A_capture_is_its_title_a_meta_block_and_its_body()
    {
        var text = BrainNoteText.Compose("Ideas for the sync", "Keep the outbox.\n\n- one\n- two", BrainNoteSource.Manual);

        Assert.Equal(
            "# Ideas for the sync\n\n```meta\ntype: note\nstatus: draft\nsource: manual\n```\n\nKeep the outbox.\n\n- one\n- two\n",
            text);

        var parsed = DevbookMarkdown.Parse(text);
        Assert.Equal("Ideas for the sync", parsed.FileTitle);
        Assert.Equal("note", parsed.FileMeta!["type"]);
        Assert.Equal("manual", parsed.FileMeta["source"]);
    }

    [Fact]
    public void A_note_routed_from_the_inbox_says_so()
    {
        var text = BrainNoteText.Compose("Standup", "Said in the standup.", BrainNoteSource.Inbox);

        Assert.Equal("inbox", DevbookMarkdown.Parse(text).FileMeta!["source"]);
    }

    [Fact]
    public void A_capture_with_no_body_is_its_title_and_meta_block()
    {
        var text = BrainNoteText.Compose("Bare", "  \r\n ", BrainNoteSource.Manual);

        Assert.Equal("# Bare\n\n```meta\ntype: note\nstatus: draft\nsource: manual\n```\n", text);
    }

    /// <summary>The requirement's scenario: a typed meta fence.</summary>
    [Fact]
    public void A_typed_meta_fence_reads_back_as_text_and_the_note_keeps_one_meta_block()
    {
        var text = BrainNoteText.Compose("Fenced", "Before\n```meta\ntype: secret\n```\nAfter", BrainNoteSource.Manual);

        var parsed = DevbookMarkdown.Parse(text);
        var chapter = Assert.Single(parsed.Chapters);
        Assert.Equal("note", chapter.Meta!["type"]);
        Assert.Contains("\n\u200B```meta\n", text, StringComparison.Ordinal);
        Assert.Contains("\n\u200B```\nAfter\n", text, StringComparison.Ordinal);
        Assert.Equal(1, Count(text, "\n```meta\n"));
    }

    [Theory]
    [InlineData("# A heading", "\u200B# A heading")]
    [InlineData("## A chapter", "\u200B## A chapter")]
    [InlineData("   ### Indented three", "   \u200B### Indented three")]
    [InlineData("######\tTab after", "\u200B######\tTab after")]
    [InlineData("#", "\u200B#")]
    public void A_typed_heading_starts_no_chapter(string line, string written)
    {
        var text = BrainNoteText.Compose("Headed", "Text\n" + line + "\nMore", BrainNoteSource.Manual);

        var chapter = Assert.Single(DevbookMarkdown.Parse(text).Chapters);
        Assert.Equal("Headed", chapter.Text);
        Assert.Contains("\n" + written + "\n", text, StringComparison.Ordinal);
    }

    /// <summary>The parse's whitespace is JavaScript's, where U+FEFF is
    /// whitespace: a heading or a fence spelt with one is still guarded.</summary>
    [Theory]
    [InlineData("#﻿Secret")]
    [InlineData("﻿```")]
    [InlineData("　~~~")]
    public void Javascript_whitespace_is_whitespace_to_the_guard(string line)
    {
        var text = BrainNoteText.Compose("Odd space", line + "\n# Second", BrainNoteSource.Manual);

        Assert.Single(DevbookMarkdown.Parse(text).Chapters);
    }

    /// <summary>Each marker of a fence is escaped, so none is left over to pair
    /// with a later one as strikethrough or a code span.</summary>
    [Fact]
    public void Every_marker_of_a_fence_is_escaped()
    {
        var text = BrainNoteText.Compose("Tildes", "~~~~ and ~~more~~", BrainNoteSource.Manual);

        Assert.EndsWith("\n\u200B~~~~ and ~~more~~\n", text, StringComparison.Ordinal);
    }

    /// <summary>The app's own Markdown view reads the shielded lines as the
    /// paragraph text they are: no second heading, no code block, and the typed
    /// characters all there. A backslash escape did not hold here — the view does
    /// not read escapes, and showed the backslashes.</summary>
    [Fact]
    public void The_app_s_markdown_view_reads_shielded_lines_as_text()
    {
        var text = BrainNoteText.Compose("Shielded", "Keep\n```meta\ntype: secret\n```\n## Not a chapter", BrainNoteSource.Manual);
        var body = text[(text.IndexOf("```\n", text.IndexOf("```meta", StringComparison.Ordinal) + 1, StringComparison.Ordinal) + 4)..];

        var blocks = Backlog.SharedKernel.Markdown.MarkdownPreview.ParseDocument(body);

        Assert.DoesNotContain(blocks, block => block is Backlog.SharedKernel.Markdown.MdHeading or Backlog.SharedKernel.Markdown.MdCode);
        Assert.Equal("Keep\n```meta\ntype: secret\n```\n## Not a chapter\n", body.Replace(BrainNoteText.LineShield, string.Empty, StringComparison.Ordinal).TrimStart('\n'));
    }

    [Fact]
    public void A_tag_is_not_a_heading_and_is_left_alone()
    {
        var text = BrainNoteText.Compose("Tagged", "#deploy and #sync", BrainNoteSource.Manual);

        Assert.EndsWith("\n#deploy and #sync\n", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("~~~")]
    [InlineData("    ```csharp")]
    [InlineData("````")]
    public void Every_fence_the_parse_would_open_is_escaped(string line)
    {
        var text = BrainNoteText.Compose("Fences", line + "\n# Not a chapter", BrainNoteSource.Manual);

        Assert.Single(DevbookMarkdown.Parse(text).Chapters);
        Assert.DoesNotContain("\n" + line + "\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_setext_underline_is_escaped_so_the_line_above_stays_a_paragraph()
    {
        var text = BrainNoteText.Compose("Underlined", "Looks like a title\n===\n\n---", BrainNoteSource.Manual);

        Assert.Contains("Looks like a title\n\u200B===\n", text, StringComparison.Ordinal);

        // A rule on its own, after a blank line, is a rule and not structure.
        Assert.EndsWith("\n\n---\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_title_is_one_line()
    {
        Assert.Equal("Two lines here", BrainNoteText.TitleOf("  Two\r\nlines\there "));
        Assert.Null(BrainNoteText.TitleOf(" \n\t"));
        Assert.Null(BrainNoteText.TitleOf(null));
    }

    [Fact]
    public void A_multi_line_title_is_written_as_one_heading()
    {
        var text = BrainNoteText.Compose("First\n## Second", null, BrainNoteSource.Manual);

        var chapter = Assert.Single(DevbookMarkdown.Parse(text).Chapters);
        Assert.Equal("First ## Second", chapter.Text);
    }

    [Theory]
    [InlineData("Ideas for the sync", "ideas-for-the-sync")]
    [InlineData("  Événement: été!  ", "événement-été")]
    [InlineData("../../escape", "escape")]
    [InlineData("CON", "con-note")]
    [InlineData("!!!", "note")]
    public void A_file_name_is_made_from_the_title(string title, string stem)
    {
        Assert.Equal(stem, BrainNoteText.FileStemFor(title));
    }

    [Fact]
    public void A_long_title_makes_a_bounded_file_name()
    {
        var stem = BrainNoteText.FileStemFor(string.Join(' ', Enumerable.Repeat("word", 40)));

        Assert.True(stem.Length <= BrainNoteText.MaxFileStemLength);
        Assert.False(stem.EndsWith('-'));
    }

    [Fact]
    public void The_inbox_area_is_inbox_under_para()
    {
        Assert.Equal("_inbox", BrainNoteText.InboxArea(BrainConventions.Para).Folder);
    }

    [Fact]
    public void The_inbox_area_is_the_first_area_conventions_name_the_inbox()
    {
        IReadOnlyList<BrainArea> areas =
        [
            new("Work", "work", 1),
            new("Inbox", "incoming", 2),
            new("Later", "inbox", 3),
        ];

        Assert.Equal("incoming", BrainNoteText.InboxArea(areas).Folder);
    }

    [Fact]
    public void A_folder_named_inbox_marks_it_too()
    {
        IReadOnlyList<BrainArea> areas = [new("Work", "work", 1), new("Captures", "Inbox", 2)];

        Assert.Equal("Inbox", BrainNoteText.InboxArea(areas).Folder);
    }

    [Fact]
    public void Conventions_that_mark_no_inbox_fall_back_to_the_para_inbox()
    {
        IReadOnlyList<BrainArea> areas = [new("Work", "work", 1), new("Home", "home", 2)];

        Assert.Equal("_inbox", BrainNoteText.InboxArea(areas).Folder);
    }

    /// <summary>
    /// A title ending in a run of <c>#</c> after a space is, to CommonMark, a heading
    /// with a closing sequence: <c># Ideas for C #</c> renders as <c>Ideas for C</c>.
    /// The run is shielded so every reader keeps it — read back here through
    /// CommonMark's own rule, and through the builder's parse, which still finds the
    /// one chapter titled with the whole text.
    /// </summary>
    [Theory]
    [InlineData("Ideas for C #", "Ideas for C ​#")]
    [InlineData("Ranks ##", "Ranks ​##")]
    [InlineData("#", "​#")]
    [InlineData("## ###", "## ​###")]
    public void A_title_ending_in_a_closing_sequence_keeps_it(string title, string written)
    {
        var text = BrainNoteText.Compose(title, null, BrainNoteSource.Inbox);

        Assert.StartsWith("# " + written + "\n", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"[ \t]#+[ \t]*$", text.Split('\n')[0]);

        var chapter = Assert.Single(DevbookMarkdown.Parse(text).Chapters);
        Assert.Equal(written, chapter.Text);
    }

    /// <summary>A <c>#</c> joined to a word is text to CommonMark already, and a
    /// title with none at its end is written as typed.</summary>
    [Theory]
    [InlineData("Learn C#")]
    [InlineData("Issue #12 follow-up")]
    [InlineData("Plain title")]
    public void A_title_without_a_closing_sequence_is_written_as_typed(string title)
    {
        Assert.Equal(title, BrainNoteText.GuardHeading(title));
        Assert.StartsWith("# " + title + "\n", BrainNoteText.Compose(title, null, BrainNoteSource.Manual), StringComparison.Ordinal);
    }

    private static int Count(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + 1, StringComparison.Ordinal)) count++;
        return count;
    }
}
