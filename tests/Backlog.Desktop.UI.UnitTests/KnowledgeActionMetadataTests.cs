using Backlog.Desktop.UI.Services;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class KnowledgeActionMetadataTests
{
    [Theory]
    [InlineData("77", "JSdotNet/Backlog", "#77", "https://github.com/JSdotNet/Backlog/issues/77")]
    [InlineData("#77", "JSdotNet/Backlog", "#77", "https://github.com/JSdotNet/Backlog/issues/77")]
    [InlineData("owner/repo#77", "JSdotNet/Backlog", "owner/repo#77", "https://github.com/owner/repo/issues/77")]
    [InlineData("https://github.com/owner/repo/issues/77", "JSdotNet/Backlog", "owner/repo#77", "https://github.com/owner/repo/issues/77")]
    public void Issue_parses_supported_forms(string value, string repository, string label, string url)
    {
        var issue = KnowledgeActionMetadata.Issue(new Dictionary<string, string> { ["knowledge-issue"] = value }, repository);

        Assert.NotNull(issue);
        Assert.Equal(label, issue.Label);
        Assert.Equal(url, issue.Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("null")]
    public void Issue_ignores_empty_and_null_values(string? value)
    {
        var metadata = value is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["knowledge-issue"] = value };

        Assert.Null(KnowledgeActionMetadata.Issue(metadata, "JSdotNet/Backlog"));
    }

    [Fact]
    public void BuildPrompt_describes_knowledge_item_context()
    {
        var prompt = KnowledgeActionMetadata.BuildPrompt(new KnowledgeActionItem(
            "Inbox capture",
            "Feature",
            ".domain/inbox/features.md",
            new Dictionary<string, string> { ["status"] = "planned" },
            "Captures inbox items."));

        Assert.Contains("Knowledge item: Inbox capture", prompt);
        Assert.Contains("Type: Feature", prompt);
        Assert.Contains("Path: .domain/inbox/features.md", prompt);
        Assert.Contains("status: planned", prompt);
        Assert.Contains("Captures inbox items.", prompt);
    }
}