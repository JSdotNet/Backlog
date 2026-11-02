
namespace Backlog.Desktop.UI.UnitTests;

public sealed class DevbookActionMetadataTests
{
    [Fact]
    public void BuildPrompt_describes_knowledge_item_context()
    {
        var prompt = DevbookActionMetadata.BuildPrompt(new DevbookActionItem(
            "Inbox capture",
            "Feature",
            ".domain/inbox/features.md",
            new Dictionary<string, string> { ["status"] = "planned" },
            "Captures inbox items."));

        Assert.Contains("Devbook item: Inbox capture", prompt);
        Assert.Contains("Type: Feature", prompt);
        Assert.Contains("Path: .domain/inbox/features.md", prompt);
        Assert.Contains("status: planned", prompt);
        Assert.Contains("Captures inbox items.", prompt);
    }
}