using Bunit;
using Backlog.Desktop.UI.Components.Pages;
using Backlog.Desktop.UI.Services;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class SettingsAiUsageTests
{
    [Fact]
    public void Ai_tab_and_usage_endpoints_are_visible_when_only_usage_metrics_enabled()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        Assert.Contains("AI", SettingsTabs(context.Component));
        Assert.Empty(context.Component.FindAll("[data-testid='usage-api-endpoints-settings']"));

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
        {
            Assert.Single(context.Component.FindAll("[data-testid='usage-api-endpoints-settings']"));
            Assert.Empty(context.Component.FindAll("[data-testid='azure-foundry-settings']"));
        });

        var claudeInput = context.Component.Find("[data-testid='claude-usage-endpoint-input']");
        claudeInput.Input(" https://claude.example.internal/v1/ ");
        claudeInput.Change();

        var githubInput = context.Component.Find("[data-testid='github-usage-endpoint-input']");
        githubInput.Input(" https://ghe.example.internal/api/v3/ ");
        githubInput.Change();

        Assert.Equal("https://claude.example.internal/v1", context.ClaudeStore.Current.ApiEndpoint);
        Assert.Equal("https://ghe.example.internal/api/v3", context.GitHub.Settings.Current.ApiEndpoint);
        Assert.Contains("AI usage API settings updated.", context.Component.Find("[data-testid='usage-api-endpoints-status']").TextContent);
    }

    [Fact]
    public void Ai_tab_shows_azure_foundry_only_when_only_ai_assistant_enabled()
    {
        using var context = RenderSettings(aiAssistantEnabled: true, usageMetricsEnabled: false);

        Assert.Contains("AI", SettingsTabs(context.Component));

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
        {
            Assert.Single(context.Component.FindAll("[data-testid='azure-foundry-settings']"));
            Assert.Empty(context.Component.FindAll("[data-testid='usage-api-endpoints-settings']"));
        });
    }

    [Fact]
    public void Ai_tab_shows_both_sections_when_usage_metrics_and_ai_assistant_are_enabled()
    {
        using var context = RenderSettings(aiAssistantEnabled: true, usageMetricsEnabled: true);

        Assert.Contains("AI", SettingsTabs(context.Component));

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
        {
            Assert.Single(context.Component.FindAll("[data-testid='azure-foundry-settings']"));
            Assert.Single(context.Component.FindAll("[data-testid='usage-api-endpoints-settings']"));
        });
    }

    private static string[] SettingsTabs(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Select(button => button.TextContent.Trim()).ToArray();

    private static void OpenAiTab(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "AI").Click();

    private static SettingsRenderContext RenderSettings(bool aiAssistantEnabled, bool usageMetricsEnabled)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-tests", Guid.NewGuid().ToString("n"));

        var store = new BacklogStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(AppFeatureSettingsStore.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatureSettingsStore.AiAssistant, aiAssistantEnabled);
        _ = features.SetEnabled(AppFeatureSettingsStore.UsageMetrics, usageMetricsEnabled);

        var azureFoundry = new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json"));
        var claude = new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json"));
        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, _) = GitHubSettings.ParseText("JSdotNet/Backlog");
        _ = githubSettings.SetRepositories(repositories);

        var github = new GitHubIntegration(githubSettings, new StubGitHubClient(), new StubProbe());

        var testContext = new BunitContext();
        testContext.Services.AddSingleton(store);
        testContext.Services.AddSingleton(features);
        testContext.Services.AddSingleton(azureFoundry);
        testContext.Services.AddSingleton(claude);
        testContext.Services.AddSingleton(github);
        testContext.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();

        var component = testContext.Render<Settings>();
        return new SettingsRenderContext(root, testContext, component, claude, github);
    }

    private sealed record SettingsRenderContext(
        string Root,
        BunitContext TestContext,
        IRenderedComponent<Settings> Component,
        ClaudeSettingsStore ClaudeStore,
        GitHubIntegration GitHub) : IDisposable
    {
        public void Dispose()
        {
            TestContext.Dispose();

            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class StubGitHubClient : IGitHubClient
    {
        public Task<GitHubIssue> CreateIssueAsync(
            GitHubRepositoryRef repository,
            string title,
            string? body,
            IEnumerable<string>? labels = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(
            GitHubRepositoryRef repository,
            int number,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not connected."));

        public void Invalidate()
        {
        }
    }
}
