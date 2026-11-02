using Backlog.SharedKernel.Markdown;

namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// A body cut into the segments a live editor edits one at a time.
/// <para>
/// The editor shows one segment as its raw source and every other one rendered,
/// and it writes the whole document back on every keystroke as the untouched
/// segments around what was typed. So the property everything rests on is the
/// round trip: the segments, joined, are the body — byte for byte, line endings
/// and all — and an edit changes the bytes it was given and nothing else.
/// </para>
/// </summary>
public class MarkdownSourceBlocksTests
{
    /// <summary>
    /// One of everything, including the markdown the parser does not model —
    /// the corpus <c>MarkdownSourceSpanTests</c> pins the spans with, because the
    /// segments are cut along exactly those spans. Raw HTML, a reference-style
    /// link, a setext heading and a fence quoting a fence are the cases a writer
    /// would mangle.
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

    /// <summary>The corpus with <c>\n</c> line ends, whatever this file was
    /// checked out with.</summary>
    private static readonly string LfCorpus = Corpus.Replace("\r\n", "\n");

    private static readonly string CrlfCorpus = LfCorpus.Replace("\n", "\r\n");

    /// <summary>The bodies the round trip has to survive: the corpus both ways
    /// round, with and without a final newline, and the shapes that end a
    /// document awkwardly or sit outside the flow. A body with mixed line ends
    /// is added only where nothing is spliced: a splice writes the document's
    /// one newline, so putting a mixed block back is an edit.</summary>
    public static TheoryData<string> Bodies => new()
    {
        LfCorpus,
        CrlfCorpus,
        LfCorpus + "\n",
        CrlfCorpus + "\r\n",
        "",
        "\n",
        "\n\n\n",
        "Just a line with no newline after it",
        "# Heading\n\n```csharp\nvar unterminated = true;\n",
        "A note.[^1]\n\n[^1]: The note.\n[^2]: Another, never referenced.\n",
        "Before.\r\n\r\n```mermaid\r\ngraph TD;\r\n\r\nA-->B;\r\n```\r\n",
        "- one\n  - nested\n    - deeper\n- two",
        "A\n   \n\t\nB",
    };

    [Theory]
    [MemberData(nameof(Bodies))]
    [InlineData("Mixed\r\nline\nends\r\n")]
    public void Joining_every_segment_gives_the_body_back(string body)
    {
        var document = MarkdownSourceBlocks.Split(body);

        Assert.Equal(body, string.Concat(document.Segments.Select(segment => segment.Text)));
        Assert.Equal(body, document.Source);
    }

    [Fact]
    public void A_null_body_is_an_empty_document()
    {
        var document = MarkdownSourceBlocks.Split(null);

        Assert.Equal("", document.Source);
        Assert.Equal("", string.Concat(document.Segments.Select(segment => segment.Text)));
    }

    /// <summary>Every line of the body belongs to exactly one segment, and the
    /// segments come in the order the lines do — so a caller can find the
    /// segment a line is in, and an edit to one segment cannot reach another.</summary>
    [Theory]
    [MemberData(nameof(Bodies))]
    [InlineData("Mixed\r\nline\nends\r\n")]
    public void Segments_tile_the_lines_in_order(string body)
    {
        var lineCount = body.Replace("\r\n", "\n").Split('\n').Length;
        var cursor = 0;

        foreach (var segment in MarkdownSourceBlocks.Split(body).Segments)
        {
            Assert.Equal(cursor, segment.Lines.StartLine);
            Assert.True(segment.Lines.IsKnown, "A segment names no lines.");

            cursor = segment.Lines.EndLineExclusive;
        }

        Assert.Equal(lineCount, cursor);
    }

    /// <summary>The blocks the segments render with are the parse's own, one per
    /// span and in its order — the footnote numbers and task indices in them are
    /// document-wide because nothing parsed a slice on its own.</summary>
    [Fact]
    public void Block_segments_carry_the_parsers_blocks_in_order()
    {
        var expected = MarkdownPreview.ParseDocument(LfCorpus)
            .Where(block => block.Source.IsKnown)
            .Select(block => (block.GetType(), block.Source));

        var actual = MarkdownSourceBlocks.Split(LfCorpus).Segments
            .Where(segment => segment.Kind == MdSourceSegmentKind.Block)
            .Select(segment => (segment.Block!.GetType(), segment.Lines));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Only_block_segments_carry_a_block()
    {
        var segments = MarkdownSourceBlocks.Split(LfCorpus).Segments;

        Assert.All(segments, segment =>
            Assert.Equal(segment.Kind == MdSourceSegmentKind.Block, segment.Block is not null));
    }

    [Fact]
    public void A_run_of_blank_lines_is_one_blank_segment()
    {
        var segments = MarkdownSourceBlocks.Split("A\n\n   \n\nB").Segments;

        Assert.Equal(
            new[]
            {
                (MdSourceSegmentKind.Block, "A\n"),
                (MdSourceSegmentKind.Blank, "\n   \n\n"),
                (MdSourceSegmentKind.Block, "B"),
            },
            segments.Select(segment => (segment.Kind, segment.Text)));
    }

    /// <summary>The definitions the parser lifts out of the flow are content
    /// nobody's span claims. They stay in the document as raw source of their
    /// own, and the note list they produce is handed over beside the segments
    /// rather than as one — it was collected from wherever the definitions sat,
    /// so it has no lines to sit at.</summary>
    [Fact]
    public void Footnote_definitions_are_one_unclaimed_segment_and_the_notes_come_separately()
    {
        var document = MarkdownSourceBlocks.Split("A note.[^1]\n\n[^1]: The note.\n[^2]: Another.");

        var unclaimed = Assert.Single(document.Segments, segment => segment.Kind == MdSourceSegmentKind.Unclaimed);
        Assert.Equal("[^1]: The note.\n[^2]: Another.", unclaimed.Text);
        Assert.Equal(new MdSourceSpan(2, 4), unclaimed.Lines);

        Assert.NotNull(document.Footnotes);
        Assert.DoesNotContain(document.Segments, segment => segment.Block is MdFootnotes);
    }

    [Fact]
    public void A_body_without_references_has_no_footnotes()
    {
        Assert.Null(MarkdownSourceBlocks.Split("Just prose.").Footnotes);
    }

    /// <summary>A blank line inside a fence is the fence's, so a diagram with a
    /// gap in it is still one block to click and edit.</summary>
    [Fact]
    public void A_fence_with_a_blank_line_inside_is_one_segment()
    {
        var segment = Assert.Single(MarkdownSourceBlocks.Split("```mermaid\ngraph TD;\n\nA-->B;\n```").Segments);

        Assert.IsType<MdCode>(segment.Block);
    }

    [Fact]
    public void An_unterminated_fence_runs_to_the_end_of_the_body()
    {
        var segments = MarkdownSourceBlocks.Split("Prose.\n\n```csharp\nvar open = true;\n\nMore.\n").Segments;

        var fence = segments[^1];
        Assert.IsType<MdCode>(fence.Block);
        Assert.Equal("```csharp\nvar open = true;\n\nMore.\n", fence.Text);
    }

    /// <summary>What the editor puts in the textarea: the segment's own lines,
    /// but not the line end that separates it from the next segment — typing
    /// at the end of a paragraph types at the end of its last line.</summary>
    [Theory]
    [InlineData("A paragraph\r\nover two lines.\r\n\r\nNext.", 0, "A paragraph\r\nover two lines.")]
    [InlineData("First.\n\nLast, with no newline", 2, "Last, with no newline")]
    [InlineData("First.\n\nLast, with one\n", 2, "Last, with one")]
    [InlineData("```csharp\nopen\n", 0, "```csharp\nopen\n")]
    [InlineData("First.\n\n", 1, "\n")]
    public void A_segments_editable_text_drops_its_final_line_end(string body, int index, string editable)
    {
        Assert.Equal(editable, MarkdownSourceBlocks.Split(body).Segments[index].Editable);
    }

    [Theory]
    [InlineData("a\nb", "\n")]
    [InlineData("a\r\nb", "\r\n")]
    [InlineData("no line end", "\n")]
    public void The_newline_is_the_one_the_document_is_written_with(string body, string newline)
    {
        Assert.Equal(newline, MarkdownSourceBlocks.Newline(body));
        Assert.Equal(newline, MarkdownSourceBlocks.Split(body).Newline);
    }

    /// <summary>Each line keeps the terminator that ended it, so the lines
    /// concatenate to the body — and there is one more line than terminators,
    /// the way the parser's own split counts them, so a span indexes both.</summary>
    [Fact]
    public void Lines_keep_their_own_terminators()
    {
        Assert.Equal(new[] { "a\r\n", "b\n", "" }, MarkdownSourceBlocks.Lines("a\r\nb\n"));
        Assert.Equal(new[] { "lone\rreturn" }, MarkdownSourceBlocks.Lines("lone\rreturn"));
    }

    /// <summary>The spans a broken parse could hand over: two blocks claiming
    /// the same line, a block behind the one before it, a block past the end of
    /// the body, and a block read from nowhere.</summary>
    private static readonly MdBlock[][] InconsistentParses =
    [
        [Paragraph(0, 2), Paragraph(1, 3)],
        [Paragraph(2, 3), Paragraph(0, 1)],
        [Paragraph(0, 1), Paragraph(2, 9)],
        [Paragraph(0, 1), new MdParagraph([])],
    ];

    /// <summary>
    /// When the spans cannot be trusted the whole body is one raw segment. A
    /// segment cut along a wrong span would lose or duplicate the author's lines
    /// on the next keystroke; one raw segment cannot lose anything, and the
    /// author can still edit all of it.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Spans_that_cannot_be_trusted_make_the_whole_body_one_unclaimed_segment(int parse)
    {
        const string body = "one\ntwo\nthree";

        var segment = Assert.Single(MarkdownSourceBlocks.Split(body, InconsistentParses[parse]).Segments);

        Assert.Equal(MdSourceSegmentKind.Unclaimed, segment.Kind);
        Assert.Equal(body, segment.Text);
        Assert.Equal(new MdSourceSpan(0, 3), segment.Lines);
        Assert.Null(segment.Block);
    }

    /// <summary>A splice changes the lines it names and nothing else: the
    /// heading, the blank lines and the paragraph after are the author's bytes,
    /// and what was typed with a textarea's <c>\n</c> comes in as the
    /// document's <c>\r\n</c>.</summary>
    [Fact]
    public void Replacing_lines_leaves_every_other_byte_alone()
    {
        const string body = "# T\r\n\r\nOld para\r\nwrapped.\r\n\r\nAfter.";

        var spliced = MarkdownSourceBlocks.Replace(body, new MdSourceSpan(2, 4), "New\npara");

        Assert.Equal("# T\r\n\r\nNew\r\npara\r\n\r\nAfter.", spliced);
    }

    [Theory]
    [InlineData("a\n\nb\n", 2, 3, "c", "a\n\nc\n")]
    [InlineData("a\n\nb", 2, 3, "c\nd", "a\n\nc\nd")]
    [InlineData("a\r\n\r\nb", 2, 3, "c", "a\r\n\r\nc")]
    [InlineData("a\nb", 0, 1, "x\r\ny", "x\ny\nb")]
    [InlineData("only", 0, 1, "", "")]
    public void The_last_lines_terminator_is_kept_and_typed_newlines_follow_the_document(
        string body, int start, int end, string typed, string expected)
    {
        Assert.Equal(expected, MarkdownSourceBlocks.Replace(body, new MdSourceSpan(start, end), typed));
    }

    /// <summary>Putting back exactly what a segment offered for editing is no
    /// edit at all — the property that makes clicking into a block and out
    /// again leave the file untouched.</summary>
    [Theory]
    [MemberData(nameof(Bodies))]
    public void Replacing_a_segment_with_its_own_editable_text_changes_nothing(string body)
    {
        foreach (var segment in MarkdownSourceBlocks.Split(body).Segments)
        {
            Assert.Equal(body, MarkdownSourceBlocks.Replace(body, segment.Lines, segment.Editable.Replace("\r\n", "\n")));
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 1)]
    [InlineData(1, 4)]
    [InlineData(-1, 1)]
    public void A_span_the_body_does_not_have_is_refused(int start, int end)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MarkdownSourceBlocks.Replace("one\ntwo\nthree", new MdSourceSpan(start, end), "x"));
    }

    private static MdParagraph Paragraph(int start, int end) =>
        new([new MdText("p")]) { Source = new MdSourceSpan(start, end) };
}
