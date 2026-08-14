using Bunit;
using Backlog.Desktop.UI.Components;
using Backlog.Desktop.UI.Services;
using Backlog.Infrastructure.GitHub;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

public class KnowledgeMetadataDisplayTests
{
    [Theory]
    [InlineData("link")]
    [InlineData("related")]
    [InlineData(" Related ")]
    public void Hides_redundant_labels_for_visible_links(string label)
    {
        Assert.False(KnowledgeMetadataDisplay.ShouldShowLabel(label));
    }

    [Theory]
    [InlineData("status")]
    [InlineData("depends-on")]
    [InlineData("owner")]
    public void Keeps_labels_that_add_meaning(string label)
    {
        Assert.True(KnowledgeMetadataDisplay.ShouldShowLabel(label));
    }

    [Fact]
    public void Domain_metadata_renders_link_targets_without_redundant_labels()
    {
        using var context = new BunitContext();
        var settings = new GitHubSettingsStore(Path.Combine(Path.GetTempPath(), "backlog-domain-panel-tests", Guid.NewGuid().ToString("n"), "github.json"));
        context.Services.AddSingleton(new DomainKnowledgeStore(settings));
        context.Services.AddSingleton(CopilotCliIntegration.Unavailable);
        context.Services.AddSingleton(new AppFeatureSettingsStore(Path.Combine(Path.GetTempPath(), "backlog-domain-panel-tests", Guid.NewGuid().ToString("n"), "features.json")));

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["related"] = ".domain/backlog/domain.md, .arc42/03-context-and-scope.md"
        };
        var links = new[]
        {
            ".domain/backlog/domain.md",
            ".arc42/03-context-and-scope.md",
            ".domain/backlog/model.md#domain-event-aiworklogged"
        };
        var view = new DomainKnowledgeView(
            "JSdotNet/Backlog",
            "D:\\repo",
            "D:\\repo\\.domain",
            null,
            new DomainKnowledgeDocument(".domain/context-map.md", "Context Map", DomainKnowledgeDocumentKind.ContextMap, "draft", metadata, string.Empty, [], [], links),
            []);

        var component = context.Render<DomainKnowledgePanel>(parameters => parameters.Add(panel => panel.View, view));
        var text = component.Markup;

        Assert.Contains(".domain/backlog/domain.md", text, StringComparison.Ordinal);
        Assert.Contains(".arc42/03-context-and-scope.md", text, StringComparison.Ordinal);
        Assert.Contains(".domain/backlog/model.md#domain-event-aiworklogged", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<strong>related</strong>", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<strong>link</strong>", text, StringComparison.OrdinalIgnoreCase);
    }
}
