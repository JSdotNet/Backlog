namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Which heading of a document a chapter link's anchor names, as the document view
/// reveals it. Both slug rules in use resolve — the app's own, which an AI usage's
/// link carries, and GitHub's, which an author copies — at a chapter's own heading
/// and at a deeper one inside it, and the text handed back is the heading as the
/// reader sees it rendered.
/// </summary>
public sealed class DocumentDevbookViewHeadingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-document-heading", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("coding-agent", "Coding agent")]
    [InlineData("review-with-copilot", "Review with `Copilot`")]
    [InlineData("pr-checks-ci", "PR checks & CI")]
    [InlineData("pr-checks--ci", "PR checks & CI")]
    [InlineData("the-rule", "The **rule**")]
    public void An_anchor_names_the_heading_it_was_made_from(string anchor, string written)
    {
        var heading = DocumentDevbookView.HeadingFor(Parse(), anchor);

        Assert.Equal(written.Replace("`", string.Empty, StringComparison.Ordinal).Replace("*", string.Empty, StringComparison.Ordinal), heading);
    }

    [Fact]
    public void An_anchor_no_heading_has_names_nothing() =>
        Assert.Null(DocumentDevbookView.HeadingFor(Parse(), "renamed-away"));

    private DocumentDevbookFile Parse()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "01-build.md");
        File.WriteAllText(path, """
            # Build

            ## Coding agent

            Hands the implementation to the agent.

            ### Review with `Copilot`

            A second pair of eyes.

            ## PR checks & CI

            ### The **rule**

            Nothing merges red.
            """);
        return DocumentDevbookParser.ParseFile(_root, path, "ai");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
    }
}
