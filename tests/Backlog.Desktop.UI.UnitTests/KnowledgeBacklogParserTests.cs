using Backlog.Desktop.UI.Services;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class KnowledgeBacklogParserTests
{
    [Fact]
    public void Reads_backlog_items_sub_items_metadata_and_diagrams()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-knowledge-tests", Guid.NewGuid().ToString("n"));
        var backlog = Path.Combine(root, ".backlog");
        Directory.CreateDirectory(backlog);
        var path = Path.Combine(backlog, "feature-knowledge.md");

        try
        {
            File.WriteAllText(path, """
                # Knowledge Feature
                ```meta
                status: draft
                related:
                  - .arc42/05-building-block-view.md
                  - .domain/context-map.md
                ```

                ## Item: Display backlog knowledge
                ```meta
                status: in-progress
                issue: 42
                depends-on: .tech/shared.md
                ```

                Render the authored work items with [links](https://example.com).

                ```mermaid
                graph TD
                    A[Backlog file] --> B[Knowledge pane]
                ```

                ### Sub-item: Parse metadata
                ```meta
                status: ready
                implements: .backlog/feature-knowledge.md#item-display-backlog-knowledge
                ```

                Keep the metadata readable.
                """);

            var concern = BacklogKnowledgeParser.ParseFile(path, root);

            Assert.Equal("Knowledge Feature", concern.Title);
            Assert.Equal("draft", concern.Metadata["status"]);
            Assert.Equal(".arc42/05-building-block-view.md, .domain/context-map.md", concern.Metadata["related"]);
            Assert.Equal(BacklogWorkItemRelativePath(root, path), concern.RelativePath);

            var item = Assert.Single(concern.Items);
            Assert.Equal("Item: Display backlog knowledge", item.Title);
            Assert.Equal("in-progress", item.Metadata["status"]);
            Assert.Contains(item.Description, block => block is MdParagraph);
            Assert.Contains(item.Description, block => block is MdCode { Language: "mermaid" });

            var subItem = Assert.Single(item.SubItems);
            Assert.Equal("Sub-item: Parse metadata", subItem.Title);
            Assert.Equal("ready", subItem.Metadata["status"]);
            Assert.Single(subItem.Description);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static string BacklogWorkItemRelativePath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.StartsWith(".", StringComparison.Ordinal) ? relative : $".{Path.DirectorySeparatorChar}{relative}";
    }
}
