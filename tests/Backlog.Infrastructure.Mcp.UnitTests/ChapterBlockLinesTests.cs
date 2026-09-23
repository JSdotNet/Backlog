using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The line range a block payload carries, and the one block that has none.
/// <para>
/// Footnotes are collected from <c>[^label]:</c> definitions scattered anywhere
/// in the body, so the block the parse emits for them was read from no line range
/// at all. The payload used to answer that with <c>StartLine</c> twice — zero to
/// zero — which is indistinguishable from a real block at the top of the file. A
/// session slicing the chapter by those coordinates cuts the title out and is
/// told nothing went wrong.
/// </para>
/// </summary>
public sealed class ChapterBlockLinesTests : IDisposable
{
    private const string ChapterPath = ".arc42/adr/0012-mcp.md";

    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("backlog-mcp-lines");

    public void Dispose() => _root.Delete(recursive: true);

    /// <summary>A block with no lines says so out loud, at both ends.</summary>
    [Fact]
    public async Task The_footnote_block_names_no_lines()
    {
        var answer = await Read(string.Join(
            '\n',
            "# The MCP host",
            string.Empty,
            "Prose with a footnote[^one].",
            string.Empty,
            "[^one]: The note itself.",
            string.Empty));

        var footnotes = Assert.Single(answer.Blocks, block => block.Kind == "footnotes");

        Assert.Null(footnotes.StartLine);
        Assert.Null(footnotes.EndLineExclusive);
    }

    /// <summary>And every block that was read from lines still names them, with
    /// the end past the start — a range, never a point.</summary>
    [Fact]
    public async Task Every_other_block_names_the_lines_it_was_read_from()
    {
        var answer = await Read(string.Join(
            '\n',
            "# The MCP host",
            string.Empty,
            "Prose with a footnote[^one].",
            string.Empty,
            "```csharp",
            "var server = new McpServer();",
            "```",
            string.Empty,
            "[^one]: The note itself.",
            string.Empty));

        Assert.All(
            answer.Blocks.Where(block => block.Kind != "footnotes"),
            block =>
            {
                Assert.NotNull(block.StartLine);
                Assert.NotNull(block.EndLineExclusive);
                Assert.True(
                    block.EndLineExclusive > block.StartLine,
                    $"Block {block.Index} ({block.Kind}) names {block.StartLine} to {block.EndLineExclusive}, "
                    + "which is no lines at all.");
            });

        // And the guard that keeps the assertion above from passing on an empty
        // set: the chapter really does have blocks that were read from lines.
        Assert.Contains(answer.Blocks, block => block.Kind != "footnotes");
    }

    private async Task<ChapterPayload> Read(string chapter)
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
            "JSdotNet/Backlog", ChapterPath, review: false, TestContext.Current.CancellationToken);
    }
}
