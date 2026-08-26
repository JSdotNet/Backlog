namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// One block of every kind the two knowledge block renderers know how to draw.
///
/// <para>Assembled rather than parsed out of a file, so the set is complete and
/// stays complete: a parser change that stopped producing one of these records
/// would quietly shrink a sample read off disk, and the golden markup would
/// still match. No knowledge file writes a section like either of these.</para>
/// </summary>
internal static class KnowledgeGoldenSamples
{
    /// <summary>The design section's blocks — <c>DesignKnowledgeView</c>'s half.
    /// Both table shapes are here because the token modifier is the only thing
    /// separating them.</summary>
    internal static IReadOnlyList<DesignKnowledgeBlock> DesignBlocks =>
    [
        new DesignKnowledgeSubheading(3, "Tokens"),
        new DesignKnowledgeParagraph(MarkdownPreview.ParseInlines(
            "A paragraph with **bold**, `code`, a #tag and a [link](https://example.com).")),
        new DesignKnowledgeList(false,
        [
            MarkdownPreview.ParseInlines("A bullet"),
            MarkdownPreview.ParseInlines("Another, with a #tag")
        ]),
        new DesignKnowledgeList(true,
        [
            MarkdownPreview.ParseInlines("First"),
            MarkdownPreview.ParseInlines("Second")
        ]),
        new DesignKnowledgeQuote(MarkdownPreview.ParseInlines("A quote that says something.")),
        new DesignKnowledgeTable(["Name", "Meaning"], [["one", "the first"], ["two", "the second"]], false),
        new DesignKnowledgeTable(["Token", "Value"], [["--surface", "#101014"]], true),
        new DesignKnowledgeCode("csharp", "var blocks = DesignKnowledge.Parse(source);"),
        // A diagram language the library draws, and one it only labels: plantuml
        // is a diagram to DiagramView but nothing renders it, so it falls back to
        // its source and no per-instance id reaches the markup.
        new DesignKnowledgeDiagram("plantuml", "@startuml\nA -> B\n@enduml"),
        new DesignKnowledgeDiagram("mermaid", "graph TD; a-->b;"),
        new DesignKnowledgeDivider()
    ];

    /// <summary>The arc42 panel's blocks — <c>Arc42KnowledgePanel</c>'s half.
    /// Three headings, because which of them draws a metadata line is a property
    /// of the record and not of the level.</summary>
    internal static IReadOnlyList<KnowledgeBlock> Arc42Blocks =>
    [
        new KnowledgeHeadingBlock(1, "Introduction and Goals",
            new KnowledgeMeta("ready", [".arc42/02-constraints.md#technical-constraints"])),
        new KnowledgeHeadingBlock(2, "Quality Goals", KnowledgeMeta.Empty),
        new KnowledgeHeadingBlock(3, "Stakeholders", new KnowledgeMeta("draft", [".domain/context-map.md"])),
        new KnowledgeParagraphBlock(MarkdownPreview.ParseInlines(
            "A paragraph with **bold**, `code`, a #tag and a [link](https://example.com).")),
        new KnowledgeListBlock(false,
        [
            MarkdownPreview.ParseInlines("A bullet"),
            MarkdownPreview.ParseInlines("Another, with a #tag")
        ]),
        new KnowledgeListBlock(true,
        [
            MarkdownPreview.ParseInlines("First"),
            MarkdownPreview.ParseInlines("Second")
        ]),
        new KnowledgeQuoteBlock(MarkdownPreview.ParseInlines("A quote that says something.")),
        new KnowledgeCodeBlock("csharp", "var document = KnowledgeMarkdownParser.Parse(path, source);"),
        new KnowledgeDiagramBlock("mermaid", "graph TD; a-->b;", "Architecture diagram 1"),
        // The reason the diagram test is a parameter rather than a constant: this
        // parser reads every `c4*` fence as a diagram, and the library's own
        // vocabulary does not.
        new KnowledgeDiagramBlock("c4context", "C4Context\ntitle System", "System"),
        new KnowledgeTableBlock(
        [
            MarkdownPreview.ParseInlines("Name | Meaning"),
            MarkdownPreview.ParseInlines("one | the first")
        ]),
        new KnowledgeDividerBlock()
    ];
}
