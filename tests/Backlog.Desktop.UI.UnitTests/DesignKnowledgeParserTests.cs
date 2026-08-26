
namespace Backlog.Desktop.UI.UnitTests;

public sealed class DesignKnowledgeParserTests
{
    [Fact]
    public void Parses_design_metadata_sections_tokens_and_diagrams()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "color-scheme.md");
        File.WriteAllText(path, """
# Color scheme
```meta
status: approved
related: [.design/design-principles.md, .arc42/10-quality-requirements.md]
```
> Dark mode only.

## Semantic colors
```meta
status: active
```

| Token | Value | Usage |
| --- | --- | --- |
| --color-primary | #F2C14E | Focus rings |

```mermaid
graph TD
    A[Token] --> B[Component]
```
""");

        var file = DesignKnowledgeParser.ParseFile(folder.Path, path);

        Assert.Equal("Color scheme", file.Title);
        Assert.Equal("approved", file.Meta.Status);

        // Parsed into references rather than kept as the strings the fence spelled.
        // That is the whole point of reading the block through the shared reader:
        // a `related` entry is an address, and the raw path was what the design
        // pane used to print back at the reader.
        Assert.Equal(
            [".design/design-principles.md", ".arc42/10-quality-requirements.md"],
            file.Meta.Related.Select(reference => reference.Raw));
        Assert.Equal("Dark mode only.", file.Summary);

        var section = Assert.Single(file.Sections);
        Assert.Equal("Semantic colors", section.Heading);
        Assert.Equal("active", section.Meta.Status);
        Assert.Contains(section.Blocks, block => block is DesignKnowledgeTable { IsTokenTable: true });
        Assert.Contains(section.Blocks, block => block is DesignKnowledgeDiagram { Language: "mermaid" });
    }

    /// <summary>
    /// A block that states nothing states nothing. The reader used to answer
    /// "unknown" for an absent status, which is a word no design file writes and
    /// which every surface then printed as though the file had.
    /// </summary>
    [Fact]
    public void A_heading_that_carries_no_block_states_no_status()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "typography-and-layout.md");
        File.WriteAllText(path, """
# Typography and layout

## Metadata lines

The record is drawn against its subject.
""");

        var file = DesignKnowledgeParser.ParseFile(folder.Path, path);

        Assert.Null(file.Meta.Status);
        Assert.Null(Assert.Single(file.Sections).Meta.Status);
    }

    [Fact]
    public void Keeps_level_two_headings_as_knowledge_sections()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "design-principles.md");
        File.WriteAllText(path, """
# Design principles

## Canonical Markdown

Markdown remains canonical behind the rich text editor.

## Keyboard equivalents

- Drag and drop needs keyboard support.
""");

        var file = DesignKnowledgeParser.ParseFile(folder.Path, path);

        Assert.Equal(["Canonical Markdown", "Keyboard equivalents"], file.Sections.Select(section => section.Heading));
        Assert.IsType<DesignKnowledgeParagraph>(Assert.Single(file.Sections[0].Blocks));
        Assert.IsType<DesignKnowledgeList>(Assert.Single(file.Sections[1].Blocks));
    }

    [Fact]
    public void A_root_document_still_declares_the_order_its_folder_is_read_in()
    {
        // The shared metadata record does not model `order`, because a directory's
        // reading order is not metadata about the chapter whose fence it happens to
        // share. .design still writes it in README.md, and this parser still reads
        // it — for itself, the way the .domain and .tech readers already read
        // theirs. Without this the pane would fall back to alphabetical and the
        // folder would open with accessibility.md.
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "README.md");
        File.WriteAllText(path, """
# Design Knowledge

```meta
status: active
order: ["design-principles.md", "color-scheme.md", "accessibility.md"]
related: [".arc42/02-constraints.md"]
```

> The checked-in record of the design guidelines.
""");

        var file = DesignKnowledgeParser.ParseFile(folder.Path, path);

        Assert.Equal(["design-principles.md", "color-scheme.md", "accessibility.md"], file.ReadingOrder);

        // Read, and not collected as an unrecognised field: the shared reader knows
        // the key and drops it, so it never reaches the record as Extra.
        Assert.Empty(file.Meta.Extra);
        Assert.Equal("active", file.Meta.Status);
    }

    [Fact]
    public void A_chapters_own_block_does_not_get_to_reorder_the_folder_it_is_in()
    {
        // Only the file-level fence is consulted. A `##` block claiming an order
        // would otherwise rearrange the sidebar from inside one section of one file.
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "color-scheme.md");
        File.WriteAllText(path, """
# Color scheme

## Tones

```meta
status: active
order: ["accessibility.md"]
```

Tones are folder-gated.
""");

        var file = DesignKnowledgeParser.ParseFile(folder.Path, path);

        Assert.Empty(file.ReadingOrder);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryFolder()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

