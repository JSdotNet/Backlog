using Backlog.Desktop.UI.Services;

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
        Assert.Equal([".design/design-principles.md", ".arc42/10-quality-requirements.md"], file.Meta.Related);
        Assert.Equal("Dark mode only.", file.Summary);

        var section = Assert.Single(file.Sections);
        Assert.Equal("Semantic colors", section.Heading);
        Assert.Equal("active", section.Meta.Status);
        Assert.Contains(section.Blocks, block => block is KnowledgeTable { IsTokenTable: true });
        Assert.Contains(section.Blocks, block => block is KnowledgeDiagram { Language: "mermaid" });
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
        Assert.IsType<KnowledgeParagraph>(Assert.Single(file.Sections[0].Blocks));
        Assert.IsType<KnowledgeList>(Assert.Single(file.Sections[1].Blocks));
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
