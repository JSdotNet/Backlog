using System.Text;

using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// What a session gets back is the author's file.
/// <para>
/// The chapter below is one of everything the read-view parser does not model —
/// a table with uneven pipes, a list indented four spaces and nested three deep,
/// raw HTML, a reference-style link, a setext heading, a mermaid fence and a
/// fence quoting a fence. Every one of them is a case a writer over the parse
/// would have quietly rewritten, so a session reading the chapter and then
/// proposing an edit would have been proposing a reformat of the file. These
/// tests are the statement that it does not: the bytes come back.
/// </para>
/// </summary>
public sealed class ChapterFidelityTests : IDisposable
{
    private const string ChapterPath = ".arc42/adr/0012-mcp.md";

    /// <summary>What a chapter's opening and closing note markers begin with,
    /// spelled out here rather than read off the implementation. The form is a
    /// contract with a reading session, and a test that asked the code what the
    /// form was could not notice it changing.</summary>
    private const string NoteOpening = "<!-- backlog:private-note";

    private const string NoteClosing = "<!-- /backlog:private-note";

    private const string Chapter = """
        # The MCP host

        ```meta
        status: active
        date: 2026-09-22
        ```

        A setext heading
        ================

        Prose with a [reference-style link][adr] and an `inline code span`.

        [adr]: https://example.invalid/adr

        | Tool |    Port    |   Notes |
        |------|:----------:|--------:|
        |  list_entries |ITaskItems| rank order |

        - The server lives in the desktop process
            - and nothing mobile references it
                - three deep
        - Scope is a repository argument

        <div class="callout">
          <p>Raw HTML, <em>unmodelled</em>.</p>
        </div>

        ```annotation
        author: someone
        body: Does this still hold?
        ```

        ```mermaid
        graph TD; Session-->Backlog;
        ```

        ````markdown
        ```csharp
        var server = new McpServer();
        ```
        ````

        The last paragraph.

        """;

    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("backlog-mcp-fidelity");

    public ChapterFidelityTests()
    {
        Directory.CreateDirectory(Path.Combine(_root.FullName, ".arc42", "adr"));

        File.WriteAllText(ChapterFile, Chapter);
    }

    /// <summary>The chapter on disk. Written as it is, never through a writer
    /// that could normalize it on the way in.</summary>
    private string ChapterFile => Path.Combine(_root.FullName, ".arc42", "adr", "0012-mcp.md");

    public void Dispose() => _root.Delete(recursive: true);

    /// <summary>
    /// The strongest statement available: with no notes to splice, a review read
    /// is the file. Not the file re-rendered, not the file normalized — the same
    /// characters in the same order.
    /// </summary>
    [Fact]
    public async Task A_review_read_of_a_chapter_with_no_notes_is_the_file()
    {
        var answer = await Read(review: true);

        Assert.Equal(Chapter, answer.Markdown);
    }

    /// <summary>
    /// An ordinary read is the file minus the lines its <c>meta</c> and
    /// <c>annotation</c> fences occupy, and nothing else: the table keeps its
    /// uneven pipes, the list its four-space indents, the raw HTML its tags, the
    /// reference-style link its brackets, the setext heading its underline, and
    /// the fence quoting a fence both its fences.
    /// </summary>
    [Fact]
    public async Task An_ordinary_read_is_the_file_minus_the_two_private_fences()
    {
        var answer = await Read(review: false);

        Assert.Equal(WithoutPrivateFences(Chapter), answer.Markdown);

        // And said plainly, in case the expectation above ever drifts: the
        // records are gone and the chapter is not.
        Assert.DoesNotContain("status: active", answer.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Does this still hold?", answer.Markdown, StringComparison.Ordinal);
        Assert.Contains("```mermaid", answer.Markdown, StringComparison.Ordinal);
        Assert.Contains("```csharp", answer.Markdown, StringComparison.Ordinal);
        Assert.Contains("|  list_entries |ITaskItems| rank order |", answer.Markdown, StringComparison.Ordinal);
        Assert.Contains("        - three deep", answer.Markdown, StringComparison.Ordinal);
        Assert.Contains("<div class=\"callout\">", answer.Markdown, StringComparison.Ordinal);
        Assert.Contains("[reference-style link][adr]", answer.Markdown, StringComparison.Ordinal);
        Assert.Contains("================", answer.Markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// With notes, a review read is still the file: take the delimited notes back
    /// out and every byte is where the author left it.
    /// </summary>
    [Fact]
    public async Task A_review_read_with_notes_is_the_file_plus_the_notes()
    {
        var table = await BlockIndexOf("| Tool |");
        var list = await BlockIndexOf("- The server lives");

        var onTable = Notes.Note(Backlog.Alias, ChapterPath, table, "Is ITaskItems still the only read?");
        var onList = Notes.Note(Backlog.Alias, ChapterPath, list, "Two lines\nof remark.");

        var answer = await Read(review: true, onTable, onList);

        Assert.Equal(Chapter, WithoutNotes(answer.Markdown));
    }

    /// <summary>
    /// A note sits immediately under the source lines of the block it is anchored
    /// to — not at the top, not in a separate section — and it is fenced off by
    /// markers no chapter writes, so a session can tell a remark from the prose
    /// without guessing.
    /// </summary>
    [Fact]
    public async Task A_note_is_spliced_under_its_own_block_and_is_unmistakable()
    {
        var table = await BlockIndexOf("| Tool |");
        var note = Notes.Note(Backlog.Alias, ChapterPath, table, "Is ITaskItems still the only read?");

        var answer = await Read(review: true, note);

        var lines = Lines(answer.Markdown);
        var opening = lines.FindIndex(line => line.StartsWith(NoteOpening, StringComparison.Ordinal));

        Assert.True(opening > 0, "The note was not spliced in at all.");

        // The table's last row, then a blank line, then the note.
        Assert.Equal("|  list_entries |ITaskItems| rank order |", Text(lines[opening - 2]));
        Assert.Equal(string.Empty, Text(lines[opening - 1]));

        Assert.Contains($"id=\"{note.Id}\"", lines[opening], StringComparison.Ordinal);
        Assert.Contains($"block=\"{table}\"", lines[opening], StringComparison.Ordinal);
        Assert.Contains("author=\"SOMEMACHINE\"", lines[opening], StringComparison.Ordinal);
        Assert.Contains("resolved=\"false\"", lines[opening], StringComparison.Ordinal);

        Assert.Equal("Is ITaskItems still the only read?", Text(lines[opening + 1]));
        Assert.StartsWith(NoteClosing, lines[opening + 2], StringComparison.Ordinal);
        Assert.Contains($"id=\"{note.Id}\"", lines[opening + 2], StringComparison.Ordinal);
    }

    /// <summary>
    /// A chapter comes back with its own line endings, the spliced notes
    /// included. Both cases are run whatever this working tree holds: the
    /// repository checks out with <c>core.autocrlf</c> on, so the fixture above
    /// is CRLF on the author's machine and LF on CI, and a test that only
    /// exercised whichever one it found would be half a test on both.
    /// </summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task A_chapter_keeps_its_line_endings_notes_and_all(string newline)
    {
        var chapter = Chapter
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", newline, StringComparison.Ordinal);

        File.WriteAllText(ChapterFile, chapter);

        var table = await BlockIndexOf("| Tool |");
        var note = Notes.Note(Backlog.Alias, ChapterPath, table, "Is ITaskItems still the only read?");

        var review = await Read(review: true, note);

        Assert.Equal(chapter, WithoutNotes(review.Markdown));
        Assert.Contains(newline + NoteOpening, review.Markdown, StringComparison.Ordinal);

        // Every newline in the answer is the document's own — the splice did not
        // leave a few of the other kind in the middle of the file.
        Assert.Equal(review.Markdown.Count(character => character == '\n'), Occurrences(review.Markdown, newline));

        var plain = await Read(review: false);

        Assert.Equal(WithoutPrivateFences(chapter), plain.Markdown);
    }

    /// <summary>A note anchored past the end of the parse still travels, at the
    /// end of the document as well as in <c>OrphanedNotes</c>. Dropping it would
    /// lose what somebody said.</summary>
    [Fact]
    public async Task A_note_anchored_nowhere_is_spliced_at_the_end()
    {
        var adrift = Notes.Note(Backlog.Alias, ChapterPath, blockIndex: 99, body: "Anchored to a block that went away.");

        var answer = await Read(review: true, adrift);

        Assert.Equal(Chapter, WithoutNotes(answer.Markdown));
        Assert.Contains(NoteOpening, answer.Markdown[Chapter.Length..], StringComparison.Ordinal);
        Assert.Single(answer.OrphanedNotes);
    }

    /// <summary>
    /// A block's own text is its lines out of the file too — the table with the
    /// pipes the author typed and the list with the indents they chose. It is
    /// what makes the block index worth having: a session can name block seven
    /// and hand back an edit to it.
    /// </summary>
    [Fact]
    public async Task A_block_carries_its_own_lines_out_of_the_file()
    {
        var answer = await Read(review: true);

        var table = answer.Blocks.Single(block => block.Kind == "table");

        // Line endings normalized on both sides here and only here: what this
        // asserts is the pipes and the indents, and the byte-for-byte claim is
        // made by the whole-document tests above. The working tree is CRLF on
        // Windows and LF on CI, and neither is the subject.
        Assert.Equal(
            "| Tool |    Port    |   Notes |\n|------|:----------:|--------:|\n|  list_entries |ITaskItems| rank order |",
            Lines(table));

        var list = answer.Blocks.Single(block => Lines(block).StartsWith("- The server lives", StringComparison.Ordinal));

        Assert.Equal(
            "- The server lives in the desktop process\n"
            + "    - and nothing mobile references it\n"
            + "        - three deep\n"
            + "- Scope is a repository argument",
            Lines(list));

        // The fence keeps its own delimiters, and the one quoting another keeps
        // the inner pair as text — which is what it is.
        Assert.Contains(answer.Blocks, block => Lines(block) == "```mermaid\ngraph TD; Session-->Backlog;\n```");
    }

    /// <summary>
    /// The indices still run over the full parse in both modes, and so do the line
    /// ranges: the block an ordinary read did emit is the same block, naming the
    /// same lines of the file, at the same number. The ranges are file coordinates
    /// in both modes, so review mode splicing notes in does not move them.
    /// </summary>
    [Fact]
    public async Task The_two_modes_agree_about_every_block_they_share()
    {
        var plain = await Read(review: false);
        var review = await Read(review: true);

        Assert.Equal(review.BlockCount, plain.BlockCount);
        Assert.True(plain.Blocks.Count < review.Blocks.Count, "The ordinary read should omit the two private fences.");

        foreach (var block in plain.Blocks)
        {
            var same = review.Blocks.Single(candidate => candidate.Index == block.Index);

            Assert.Equal(block.Kind, same.Kind);
            Assert.Equal(block.StartLine, same.StartLine);
            Assert.Equal(block.EndLineExclusive, same.EndLineExclusive);
            Assert.Equal(block.Language, same.Language);
        }
    }

    /// <summary>The chapter minus the lines of its <c>meta</c> and
    /// <c>annotation</c> fences, worked out here rather than asked of the code:
    /// an expectation derived from the implementation is not an
    /// expectation.</summary>
    private static string WithoutPrivateFences(string chapter)
    {
        var lines = Lines(chapter);
        var kept = new StringBuilder();

        for (var index = 0; index < lines.Count; index++)
        {
            if (Text(lines[index]) is not ("```meta" or "```annotation"))
            {
                kept.Append(lines[index]);
                continue;
            }

            index++;

            while (index < lines.Count && Text(lines[index]) != "```") index++;
        }

        return kept.ToString();
    }

    /// <summary>The document with every delimited note taken back out, blank line
    /// and all. The inverse of the splice, and nothing else.</summary>
    private static string WithoutNotes(string markdown)
    {
        var lines = Lines(markdown);
        var kept = new List<string>();

        for (var index = 0; index < lines.Count; index++)
        {
            if (!lines[index].StartsWith(NoteOpening, StringComparison.Ordinal))
            {
                kept.Add(lines[index]);
                continue;
            }

            // The blank line above the marker was put there by the splice too.
            if (kept.Count > 0 && Text(kept[^1]).Length == 0) kept.RemoveAt(kept.Count - 1);

            while (index < lines.Count && !lines[index].StartsWith(NoteClosing, StringComparison.Ordinal)) index++;
        }

        return string.Concat(kept);
    }

    /// <summary>
    /// The value's lines, each still carrying the newline that ended it, so that
    /// putting them back together is the value again — <c>\r\n</c> and all. These
    /// tests are about bytes, and a helper that normalized line endings on the
    /// way in would be the one thing that could hide the bug they exist to
    /// catch. The repository checks out with <c>core.autocrlf</c> on, so the
    /// fixture above really is CRLF on a Windows working tree and LF on CI.
    /// </summary>
    private static List<string> Lines(string value)
    {
        var lines = new List<string>();
        var start = 0;

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '\n') continue;

            lines.Add(value[start..(index + 1)]);
            start = index + 1;
        }

        lines.Add(value[start..]);

        return lines;
    }

    /// <summary>One line without the terminator it carries.</summary>
    private static string Text(string line) => line.TrimEnd('\n').TrimEnd('\r');

    /// <summary>Line endings flattened, for the one assertion whose subject is
    /// not the line endings.</summary>
    private static string Unix(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>
    /// A block's own lines, cut out of the chapter by the range the payload names.
    /// The payload carries coordinates rather than a second copy of the text, so
    /// this is where the claim "block seven is these bytes" is actually made — and
    /// it is made against the file, which is what those coordinates address.
    /// </summary>
    private static string Lines(ChapterBlockPayload block)
    {
        var lines = Unix(Chapter).Split('\n');

        // Asserted rather than defaulted: a block with no line range is the
        // footnote block and only it, and a test that quietly cut lines 0 to 0
        // for one would be asserting against the top of the chapter while
        // claiming to assert against the block.
        Assert.NotNull(block.StartLine);
        Assert.NotNull(block.EndLineExclusive);

        return string.Join('\n', lines[block.StartLine.Value..block.EndLineExclusive.Value]);
    }

    private static int Occurrences(string value, string needle)
    {
        var count = 0;
        var at = value.IndexOf(needle, StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = value.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private async Task<int> BlockIndexOf(string opening)
    {
        var answer = await Read(review: true);

        return answer.Blocks.Single(block => Lines(block).StartsWith(opening, StringComparison.Ordinal)).Index;
    }

    private Task<ChapterPayload> Read(bool review, params DevbookAnnotation[] notes)
    {
        var tools = new DevbookTools(
            new FakeDevbookFolderSource(_root.FullName, new DevbookFolderSetting(".arc42", "Architecture", ".arc42")),
            new FakeDevbookAnnotationStore(notes),
            new FakeRepositoryDirectory([Backlog]));

        return tools.ReadKnowledgeChapterAsync(
            "JSdotNet/Backlog",
            ChapterPath,
            review,
            TestContext.Current.CancellationToken);
    }
}
