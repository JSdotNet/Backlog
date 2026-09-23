using System.Text;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The lines each block was read from.
/// <para>
/// The parse is lossy on purpose — a table comes back with even pipes, a nested
/// list with two-space indents, and the markdown it does not model degrades to
/// prose — so anything that has to hand an author's own bytes back cannot write
/// the model out again. <see cref="MdBlock.Source"/> is what makes that possible,
/// and the property it has to hold is this one: the spans cover the body once,
/// in order, with nothing straddling and nothing swallowed.
/// </para>
/// </summary>
public class MarkdownSourceSpanTests
{
    /// <summary>
    /// One of everything, including the markdown the parser does not model. Raw
    /// HTML, a reference-style link, a setext heading and a fence quoting a fence
    /// are exactly the cases a writer would mangle, so they are the cases the
    /// spans have to carry.
    /// </summary>
    private const string Corpus = """
        # The chapter

        A paragraph that
        wraps over two lines.

        ## A setext heading
        -------------------

        > A quotation
        > over two lines.

        | Tool | Port | Notes |
        |---|:---:|---:|
        | list_entries | ITaskItems | rank order |
        | get_roadmap | IRoadmapPlanning | by alias |

        - A bullet
          that wraps
          - nested under it
          1. and a numbered run
        - [x] a checklist item

        <div class="callout">
          <p>Raw HTML the parser does not model.</p>
        </div>

        A [reference-style link][adr] and a footnote.[^1]

        [adr]: https://example.invalid/adr
        [^1]: The note itself.

        ```meta
        status: active
        ```

        ```mermaid
        graph TD; Session-->Backlog;
        ```

        ````markdown
        ```csharp
        var server = new McpServer();
        ```
        ````

        ---

        1. One
        2. Two

        The last paragraph.
        """;

    /// <summary>
    /// The property everything else rests on: walk the blocks in order, emit the
    /// lines before each one and then its own, and the body comes back. It fails
    /// the moment a span runs backwards, overlaps its neighbour or names a line
    /// twice — and a gap cannot swallow anything, because the walk emits the
    /// lines no block claimed.
    /// </summary>
    [Fact]
    public void Concatenating_every_span_and_the_gaps_reproduces_the_body()
    {
        var body = Normalized(Corpus);

        Assert.Equal(body, Reassemble(body));
    }

    /// <summary>The same, for the shapes a document tends to end on: an
    /// unterminated fence, a body with no trailing newline, an empty body.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("Just a line with no newline after it")]
    [InlineData("# Heading\n\n```csharp\nvar unterminated = true;\n")]
    [InlineData("\n\n\n")]
    [InlineData("- one\n- two")]
    public void Awkward_bodies_come_back_whole_too(string body)
    {
        Assert.Equal(body, Reassemble(body));
    }

    /// <summary>Spans run forward and never straddle. Stated on its own because
    /// the round trip above would also pass on spans that were merely
    /// adjacent-and-wrong, and this is the invariant a caller stripping one block
    /// out of the source actually relies on.</summary>
    [Fact]
    public void Spans_run_forward_and_never_overlap()
    {
        var body = Normalized(Corpus);
        var lineCount = body.Split('\n').Length;
        var cursor = 0;

        foreach (var block in MarkdownPreview.ParseDocument(body))
        {
            if (!block.Source.IsKnown) continue;

            Assert.True(
                block.Source.StartLine >= cursor,
                $"{block.GetType().Name} starts at {block.Source.StartLine}, behind the block before it.");

            Assert.True(
                block.Source.EndLineExclusive <= lineCount,
                $"{block.GetType().Name} ends at {block.Source.EndLineExclusive}, past the body's {lineCount} lines.");

            cursor = block.Source.EndLineExclusive;
        }
    }

    /// <summary>A fence's span covers its own delimiters, which is what lets a
    /// caller lift the whole block out of the source rather than its body and a
    /// pair of orphaned backtick lines.</summary>
    [Fact]
    public void A_fence_covers_its_delimiters()
    {
        var body = "Before.\n\n```meta\nstatus: active\n```\n\nAfter.";

        var fence = Assert.IsType<MdCode>(MarkdownPreview.ParseDocument(body).Single(block => block is MdCode));

        Assert.Equal(new MdSourceSpan(2, 5), fence.Source);
        Assert.Equal("```meta\nstatus: active\n```", Slice(body, fence.Source));
    }

    /// <summary>An unterminated fence runs to the end of the body, and its span
    /// says so rather than naming a closing line that is not there.</summary>
    [Fact]
    public void An_unterminated_fence_ends_with_the_body()
    {
        var body = "```csharp\nvar unterminated = true;\n";

        var fence = Assert.IsType<MdCode>(MarkdownPreview.ParseDocument(body).Single());

        Assert.Equal(3, fence.Source.EndLineExclusive);
    }

    /// <summary>A table's span is the header, the delimiter row and every body
    /// row — one block over the lines that made it.</summary>
    [Fact]
    public void A_table_covers_its_header_delimiter_and_rows()
    {
        var body = "| a | b |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |\n\nAfter.";

        var table = MarkdownPreview.ParseDocument(body).OfType<MdTable>().Single();

        Assert.Equal(new MdSourceSpan(0, 4), table.Source);
    }

    /// <summary>A wrapped list line is part of the item above it, so the list's
    /// span covers the continuation too.</summary>
    [Fact]
    public void A_list_covers_the_lines_its_items_were_wrapped_over()
    {
        var body = "- A bullet\n  that wraps\n- Another\n\nAfter.";

        var list = MarkdownPreview.ParseDocument(body).OfType<MdList>().Single();

        Assert.Equal(new MdSourceSpan(0, 3), list.Source);
    }

    /// <summary>A nested list sits inside its parent's span. The one place spans
    /// contain one another — they still never straddle.</summary>
    [Fact]
    public void A_nested_list_sits_inside_its_parent()
    {
        var body = "- one\n  - nested\n- two";

        var list = MarkdownPreview.ParseDocument(body).OfType<MdList>().Single();
        var nested = list.Items[0].Nested.Single();

        Assert.Equal(new MdSourceSpan(0, 3), list.Source);
        Assert.Equal(new MdSourceSpan(1, 2), nested.Source);
    }

    /// <summary>A heading that took a metadata line with it covers both, so
    /// nothing is left behind when the heading is lifted out.</summary>
    [Fact]
    public void A_heading_covers_the_metadata_line_it_read()
    {
        var body = "## A sub-item\n<!-- meta -->\n\nProse.";

        var blocks = MarkdownPreview.Parse(body, inheritedArea: null, new CommentMetadataReader());
        var subItem = blocks.OfType<MdSubItem>().Single();

        // The heading's two lines, plus the paragraph folded under it.
        Assert.Equal(0, subItem.Source.StartLine);
        Assert.Equal(4, subItem.Source.EndLineExclusive);
        Assert.Equal(new MdSourceSpan(3, 4), subItem.Children.Single().Source);
    }

    /// <summary>The footnote block is collected from wherever in the body the
    /// definitions were written, so it names no lines rather than claiming a
    /// range it does not own.</summary>
    [Fact]
    public void The_footnote_block_names_no_lines()
    {
        var body = "A footnote.[^1]\n\n[^1]: The note.";

        var footnotes = MarkdownPreview.ParseDocument(body).OfType<MdFootnotes>().Single();

        Assert.False(footnotes.Source.IsKnown);
        Assert.Equal(MdSourceSpan.None, footnotes.Source);
    }

    /// <summary>A block built by hand — a fixture, a storybook sample — is
    /// unchanged by the span's presence.</summary>
    [Fact]
    public void A_block_built_by_hand_has_no_span()
    {
        var block = new MdParagraph(MarkdownPreview.ParseInlines("By hand."));

        Assert.False(block.Source.IsKnown);
        Assert.Equal(0, block.Source.LineCount);
    }

    /// <summary>The body as the parser reads it: <c>\r\n</c> normalized, which is
    /// what the span indices index into.</summary>
    private static string Normalized(string body) => body.Replace("\r\n", "\n");

    private static string Slice(string body, MdSourceSpan span) =>
        string.Join('\n', Normalized(body).Split('\n')[span.StartLine..span.EndLineExclusive]);

    private static string Reassemble(string body)
    {
        var lines = Normalized(body).Split('\n');
        var builder = new StringBuilder();
        var cursor = 0;

        void Take(int from, int to)
        {
            for (var index = from; index < to; index++)
            {
                if (builder.Length > 0 || index > 0) builder.Append('\n');
                builder.Append(lines[index]);
            }
        }

        foreach (var block in MarkdownPreview.ParseDocument(body))
        {
            if (!block.Source.IsKnown) continue;

            // Everything the blocks left behind — blank lines, and the footnote
            // definitions the parser lifts out of the flow — then the block's own
            // lines. A span that ran backwards would take this negative.
            Assert.True(block.Source.StartLine >= cursor, "A span starts behind the one before it.");

            Take(cursor, block.Source.StartLine);
            Take(block.Source.StartLine, block.Source.EndLineExclusive);

            cursor = block.Source.EndLineExclusive;
        }

        Take(cursor, lines.Length);

        return builder.ToString();
    }

    /// <summary>A metadata line, spelled the way this test's fixture spells one.
    /// The parser only knows that a line can carry metadata; what it looks like
    /// belongs to whoever is editing.</summary>
    private sealed class CommentMetadataReader : IMarkdownMetadataReader
    {
        public bool IsMetadataLine(string line) => line.TrimStart().StartsWith("<!--", StringComparison.Ordinal);

        public MarkdownMetadata Read(string line) => MarkdownMetadata.None;
    }
}
