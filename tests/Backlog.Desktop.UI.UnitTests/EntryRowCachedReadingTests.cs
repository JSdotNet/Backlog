namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The body and the steps the detail pane draws are read off the row once per
/// text instead of parsed on every render. These hold that reading to the text
/// as it stands: the same strings the parser gives, and a new reading as soon as
/// the text moves.
/// </summary>
public sealed class EntryRowCachedReadingTests
{
    [Fact]
    public void The_body_is_the_text_under_the_title_and_metadata_line()
    {
        var row = new EntryRow { RawText = "# Ship it\n`task` `!ready`\n\nWrite the notes.\n\n## Step one\nDo it.\n" };

        Assert.Equal(EntryTextParser.Parse(row.RawText).Body, row.PreviewBody);
        Assert.StartsWith("Write the notes.", row.PreviewBody, StringComparison.Ordinal);
    }

    [Fact]
    public void The_steps_titles_and_notes_follow_the_text_as_it_is_typed()
    {
        var row = new EntryRow { RawText = "# Ship it\n\n## [x] Step one\n`task` `!done`\nDone already.\n" };

        EntryTextParser.SubItemTitleAndNote[] before = [new("Step one", "Done already.")];
        Assert.Equal(before, row.PreviewStepTexts);

        row.RawText += "\n## Step two\nNext.\n";

        EntryTextParser.SubItemTitleAndNote[] after =
        [
            new("Step one", "Done already."),
            new("Step two", "Next.")
        ];
        Assert.Equal(after, row.PreviewStepTexts);
    }
}
