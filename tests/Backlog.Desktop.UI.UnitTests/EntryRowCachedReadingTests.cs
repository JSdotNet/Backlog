namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The body the detail pane's editor draws is read off the row's cached parse
/// instead of parsed on every render. This holds that reading to the text: the
/// same string the parser gives.
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
}
