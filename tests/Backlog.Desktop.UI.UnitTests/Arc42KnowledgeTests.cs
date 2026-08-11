using Backlog.Desktop.UI.Services;

namespace Backlog.Desktop.UI.UnitTests;

public class Arc42KnowledgeTests
{
    private static string PlainText(IEnumerable<MdInline> inlines) =>
        string.Concat(inlines.Select(i => i switch
        {
            MdText t => t.Text,
            MdStrong s => s.Text,
            MdEm e => e.Text,
            MdCodeSpan c => c.Text,
            MdTag g => "#" + g.Tag,
            MdLink l => l.Text,
            _ => string.Empty
        }));

    [Fact]
    public void Keeps_level_two_headings_as_architecture_headings()
    {
        var document = KnowledgeMarkdownParser.Parse(".arc42/05-building-block-view.md", "# Building Block View\n\n## Level 1\n\nDetails");

        Assert.Equal("Building Block View", document.Title);
        Assert.Contains(document.Blocks, block => block is KnowledgeSubheading { Level: 2, Text: "Level 1" });
        Assert.DoesNotContain(document.Blocks, block => block.GetType().Name.Contains("SubItem", StringComparison.Ordinal));
    }

    [Fact]
    public void Reads_heading_metadata_status_and_related_links()
    {
        var document = KnowledgeMarkdownParser.Parse(
            ".arc42/01-introduction-and-goals.md",
            "# Introduction\n```meta\nstatus: active\nrelated: [adr/0001-test.md, .tech/shared.md]\n```\n\nText");

        Assert.Equal("active", document.Metadata.Status);
        Assert.Equal(["adr/0001-test.md", ".tech/shared.md"], document.Metadata.Related);
    }

    [Fact]
    public void Turns_mermaid_fences_into_reusable_diagram_blocks()
    {
        var document = KnowledgeMarkdownParser.Parse(
            ".arc42/05-building-block-view.md",
            "# Building Block View\n\n```mermaid\nC4Context\ntitle System Context\n```\n\nAfter");

        var diagram = Assert.IsType<KnowledgeDiagram>(document.Blocks.Single(block => block is KnowledgeDiagram));
        Assert.Equal("mermaid", diagram.Language);
        Assert.Contains("title System Context", diagram.Source);
        Assert.Equal(1, document.DiagramCount);
    }

    [Fact]
    public async Task Reads_arc42_index_order_when_available()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-arc42-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".arc42", "_meta"));
            File.WriteAllText(Path.Combine(root, ".arc42", "01-first.md"), "# First");
            File.WriteAllText(Path.Combine(root, ".arc42", "02-second.md"), "# Second");
            File.WriteAllText(Path.Combine(root, ".arc42", "_meta", "index.json"), """
                {
                  "entries": [
                    { "type": "file", "path": ".arc42/02-second.md" },
                    { "type": "file", "path": ".arc42/01-first.md" }
                  ]
                }
                """);

            var catalog = await Arc42KnowledgeReader.LoadAsync(root);

            Assert.True(catalog.Exists);
            Assert.Equal(["Second", "First"], catalog.Documents.Select(document => document.Title));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Parses_ordinary_architecture_prose_as_readable_blocks()
    {
        var document = KnowledgeMarkdownParser.Parse(".arc42/08-crosscutting-concepts.md", "# Concepts\n\nA **safe** [link](https://example.com).\n\n- one\n- two");

        var paragraph = Assert.IsType<KnowledgeParagraph>(document.Blocks[1]);
        Assert.Equal("A safe link.", PlainText(paragraph.Content));
        var list = Assert.IsType<KnowledgeList>(document.Blocks[2]);
        Assert.Equal(["one", "two"], list.Items.Select(PlainText));
    }
}

