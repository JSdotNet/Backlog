using Backlog.Desktop.UI.Services;
using Backlog.Infrastructure.GitHub;

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
        Assert.Contains(document.Blocks, block => block is KnowledgeHeadingBlock { Level: 2, Text: "Level 1" });
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

        var diagram = Assert.IsType<KnowledgeDiagramBlock>(document.Blocks.Single(block => block is KnowledgeDiagramBlock));
        Assert.Equal("mermaid", diagram.Language);
        Assert.Equal("System Context", diagram.Title);
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
    public async Task Counts_only_adr_and_tdr_files_as_decision_records()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-arc42-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".arc42", "_meta"));
            Directory.CreateDirectory(Path.Combine(root, ".arc42", "adr"));
            Directory.CreateDirectory(Path.Combine(root, ".arc42", "tdr"));
            File.WriteAllText(Path.Combine(root, ".arc42", "01-introduction-and-goals.md"), """
                # Intro
                ```meta
                status: active
                ```
                """);
            File.WriteAllText(Path.Combine(root, ".arc42", "adr", "0001-test.md"), """
                # ADR 0001
                ```meta
                status: accepted
                ```
                """);
            File.WriteAllText(Path.Combine(root, ".arc42", "tdr", "0001-test.md"), """
                # TDR 0001
                ```meta
                status: draft
                ```
                """);
            File.WriteAllText(Path.Combine(root, ".arc42", "_meta", "index.json"), """
                {
                  "entries": [
                    { "type": "file", "path": ".arc42/01-introduction-and-goals.md" },
                    { "type": "directory", "path": ".arc42/adr", "children": [
                      { "type": "file", "path": ".arc42/adr/0001-test.md" }
                    ] },
                    { "type": "directory", "path": ".arc42/tdr", "children": [
                      { "type": "file", "path": ".arc42/tdr/0001-test.md" }
                    ] }
                  ]
                }
                """);

            var catalog = await Arc42KnowledgeReader.LoadAsync(root);

            Assert.Equal(2, catalog.DecisionRecordCount);
            Assert.False(Arc42KnowledgeCatalog.IsDecisionRecord(".arc42/01-introduction-and-goals.md"));
            Assert.True(Arc42KnowledgeCatalog.IsDecisionRecord(".arc42/adr/0001-test.md"));
            Assert.True(Arc42KnowledgeCatalog.IsDecisionRecord(".arc42/tdr/0001-test.md"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public async Task Updates_arc42_chapter_status_metadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-arc42-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".arc42"));
            File.WriteAllText(Path.Combine(root, ".arc42", "04-solution-strategy.md"), """
                # Solution Strategy

                ```meta
                status: active
                related: [.tech/shared.md]
                ```

                Strategy text.
                """);
            var store = ConfiguredSettings(root);
            var knowledge = new Arc42KnowledgeStore(new KnowledgeFolderSource(store));

            await knowledge.UpdateStatusAsync("backlog", ".arc42/04-solution-strategy.md", "adopted");
            var catalog = await knowledge.LoadAsync("backlog");

            var document = Assert.Single(catalog.Documents);
            Assert.Equal("adopted", document.Status);
            Assert.Contains("status: adopted", File.ReadAllText(Path.Combine(root, ".arc42", "04-solution-strategy.md")));
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

        var paragraph = Assert.IsType<KnowledgeParagraphBlock>(document.Blocks[1]);
        Assert.Equal("A safe link.", PlainText(paragraph.Content));
        var list = Assert.IsType<KnowledgeListBlock>(document.Blocks[2]);
        Assert.Equal(["one", "two"], list.Items.Select(PlainText));
    }

    private static GitHubSettingsStore ConfiguredSettings(string repo)
    {
        var path = Path.Combine(repo, "github.json");
        var settings = new GitHubSettingsStore(path);
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        settings.SetRepositories(repositories);
        settings.SetCloneDirectory("backlog", repo);
        return settings;
    }

}

