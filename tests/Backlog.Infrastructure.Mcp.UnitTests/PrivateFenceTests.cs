using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// A private fence is cut whole, whatever its body holds.
/// <para>
/// <b>What this is about.</b> An ordinary read advertises a chapter with no
/// <c>meta</c> record and no <c>annotation</c> fence in it, and a session acts on
/// that: the notes are somebody's private remarks and the record is not prose.
/// The cut is by line range, so it is only ever as good as the range — and the
/// range came from a parser that ended a fence at the first later line opening
/// with three backticks. A note quoting a code sample therefore ended at the
/// sample, and everything after it — the rest of the note's YAML, and the two
/// lines that close it — survived into text that said it carried none.
/// </para>
/// <para>
/// The delimiting rule is now the one the devbook convention's own writer uses
/// (<c>annotations.mjs</c>: a run of the same marker, at least as long as the
/// opener, with nothing after it), so the two agree about where a note ends.
/// </para>
/// </summary>
public sealed class PrivateFenceTests : IDisposable
{
    private const string ChapterPath = ".arc42/adr/0012-mcp.md";

    /// <summary>A word that appears only inside the private fences. Any of it in
    /// an ordinary read is a leak.</summary>
    private const string Secret = "unresolved-private-remark";

    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("backlog-mcp-fences");

    public void Dispose() => _root.Delete(recursive: true);

    /// <summary>
    /// A note whose <c>body:</c> quotes a fenced code sample, written the way
    /// CommonMark and <c>annotations.mjs</c> both require: an outer fence longer
    /// than the one it holds.
    /// </summary>
    [Fact]
    public async Task An_annotation_fence_quoting_a_code_sample_is_cut_whole()
    {
        var chapter = string.Join(
            '\n',
            "# The MCP host",
            string.Empty,
            "Prose the author wrote.",
            string.Empty,
            "````annotation",
            "author: someone",
            "date: 2026-09-22",
            "body: |",
            "  This is wrong, and here is what it should say:",
            string.Empty,
            "  ```csharp",
            "  var server = new McpServer();",
            "  ```",
            string.Empty,
            $"  status: {Secret}",
            "````",
            string.Empty,
            "The last paragraph.",
            string.Empty);

        var answer = await Read(chapter);

        Assert.DoesNotContain(Secret, answer.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("annotation", answer.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("author: someone", answer.Markdown, StringComparison.Ordinal);

        // And the author's own chapter is all still there, which is the other
        // half: a cut that took the paragraphs with it would be no better.
        Assert.Contains("Prose the author wrote.", answer.Markdown, StringComparison.Ordinal);
        Assert.Contains("The last paragraph.", answer.Markdown, StringComparison.Ordinal);
        Assert.Contains("# The MCP host", answer.Markdown, StringComparison.Ordinal);
    }

    /// <summary>The other way a fence holding fences is written. It was not
    /// recognised as a fence at all, so none of it was ever cut — the whole note
    /// travelled as prose.</summary>
    [Fact]
    public async Task A_tilde_delimited_private_fence_is_cut()
    {
        var chapter = string.Join(
            '\n',
            "# The MCP host",
            string.Empty,
            "~~~meta",
            "status: active",
            $"note: {Secret}",
            "~~~",
            string.Empty,
            "~~~annotation",
            "author: someone",
            $"body: {Secret}",
            "~~~",
            string.Empty,
            "The last paragraph.",
            string.Empty);

        var answer = await Read(chapter);

        Assert.DoesNotContain(Secret, answer.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("status: active", answer.Markdown, StringComparison.Ordinal);
        Assert.Contains("The last paragraph.", answer.Markdown, StringComparison.Ordinal);
    }

    /// <summary>And a review read still hands back the file untouched, both
    /// fences included — the cut is what <c>review:false</c> means, not what the
    /// parser does.</summary>
    [Fact]
    public async Task A_review_read_keeps_the_whole_fence()
    {
        var chapter = string.Join(
            '\n',
            "# The MCP host",
            string.Empty,
            "````annotation",
            "author: someone",
            "body: |",
            "  ```csharp",
            "  var x = 1;",
            "  ```",
            $"  status: {Secret}",
            "````",
            string.Empty);

        var answer = await Read(chapter, review: true);

        Assert.Equal(chapter, answer.Markdown);
        Assert.Contains(Secret, answer.Markdown, StringComparison.Ordinal);
    }

    private async Task<ChapterPayload> Read(string chapter, bool review = false)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root.FullName, ".arc42", "adr"));

        await File.WriteAllTextAsync(
            Path.Combine(folder.FullName, "0012-mcp.md"),
            chapter,
            TestContext.Current.CancellationToken);

        var tools = new DevbookTools(
            new FakeDevbookFolderSource(_root.FullName, new DevbookFolderSetting(".arc42", "Architecture", ".arc42")),
            new FakeDevbookAnnotationStore(),
            new FakeRepositoryDirectory([Backlog]));

        return await tools.ReadKnowledgeChapterAsync(
            "JSdotNet/Backlog", ChapterPath, review, TestContext.Current.CancellationToken);
    }
}
