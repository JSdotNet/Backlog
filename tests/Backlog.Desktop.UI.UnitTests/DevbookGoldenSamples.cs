namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// One block of every kind the two knowledge block renderers know how to draw.
///
/// <para>Assembled rather than parsed out of a file, so the set is complete and
/// stays complete: a parser change that stopped producing one of these records
/// would quietly shrink a sample read off disk, and the golden markup would
/// still match. No knowledge file writes a section like either of these.</para>
/// </summary>
internal static class DevbookGoldenSamples
{
    /// <summary>The design section's blocks — <c>DesignDevbookView</c>'s half.
    /// Both table shapes are here because the token modifier is the only thing
    /// separating them.</summary>
    internal static IReadOnlyList<DocumentDevbookBlock> DesignBlocks =>
    [
        new DocumentDevbookSubheading(3, "Tokens"),
        new DocumentDevbookParagraph(MarkdownPreview.ParseInlines(
            "A paragraph with **bold**, `code`, a #tag and a [link](https://example.com).")),
        new DocumentDevbookList(false,
        [
            MarkdownPreview.ParseInlines("A bullet"),
            MarkdownPreview.ParseInlines("Another, with a #tag")
        ]),
        new DocumentDevbookList(true,
        [
            MarkdownPreview.ParseInlines("First"),
            MarkdownPreview.ParseInlines("Second")
        ]),
        new DocumentDevbookQuote(MarkdownPreview.ParseInlines("A quote that says something.")),
        new DocumentDevbookTable(["Name", "Meaning"], [["one", "the first"], ["two", "the second"]], false),
        new DocumentDevbookTable(["Token", "Value"], [["--surface", "#101014"]], true),
        new DocumentDevbookCode("csharp", "var blocks = DesignDevbook.Parse(source);"),
        // A diagram language the library draws, and one it only labels: plantuml
        // is a diagram to DiagramView but nothing renders it, so it falls back to
        // its source and no per-instance id reaches the markup.
        new DocumentDevbookDiagram("plantuml", "@startuml\nA -> B\n@enduml"),
        new DocumentDevbookDiagram("mermaid", "graph TD; a-->b;"),
        new DocumentDevbookDivider()
    ];

    /// <summary>The arc42 panel's blocks — <c>Arc42DevbookPanel</c>'s half.
    /// Three headings, because which of them draws a metadata line is a property
    /// of the record and not of the level.</summary>
    internal static IReadOnlyList<DevbookBlock> Arc42Blocks =>
    [
        new DevbookHeadingBlock(1, "Introduction and Goals",
            new DevbookMeta("ready", [".arc42/02-constraints.md#technical-constraints"])),
        new DevbookHeadingBlock(2, "Quality Goals", DevbookMeta.Empty),
        new DevbookHeadingBlock(3, "Stakeholders", new DevbookMeta("draft", [".domain/context-map.md"])),
        new DevbookParagraphBlock(MarkdownPreview.ParseInlines(
            "A paragraph with **bold**, `code`, a #tag and a [link](https://example.com).")),
        new DevbookListBlock(false,
        [
            MarkdownPreview.ParseInlines("A bullet"),
            MarkdownPreview.ParseInlines("Another, with a #tag")
        ]),
        new DevbookListBlock(true,
        [
            MarkdownPreview.ParseInlines("First"),
            MarkdownPreview.ParseInlines("Second")
        ]),
        new DevbookQuoteBlock(MarkdownPreview.ParseInlines("A quote that says something.")),
        new DevbookCodeBlock("csharp", "var document = DevbookMarkdownParser.Parse(path, source);"),
        new DevbookDiagramBlock("mermaid", "graph TD; a-->b;", "Architecture diagram 1"),
        // The reason the diagram test is a parameter rather than a constant: this
        // parser reads every `c4*` fence as a diagram, and the library's own
        // vocabulary does not.
        new DevbookDiagramBlock("c4context", "C4Context\ntitle System", "System"),
        new DevbookTableBlock(
        [
            MarkdownPreview.ParseInlines("Name | Meaning"),
            MarkdownPreview.ParseInlines("one | the first")
        ]),
        new DevbookDividerBlock()
    ];
}
