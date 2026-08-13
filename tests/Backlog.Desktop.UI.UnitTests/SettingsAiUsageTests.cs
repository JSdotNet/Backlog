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
    public void Usage_endpoint_settings_stay_available_without_ai_assistant()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        Assert.Single(context.Component.FindAll("[data-testid='usage-api-endpoints-settings']"));

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
    public void Azure_foundry_settings_stay_hidden_when_ai_assistant_disabled()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        var tabs = context.Component.FindAll(".settings-tabs button").Select(button => button.TextContent.Trim()).ToArray();
        Assert.DoesNotContain("AI", tabs);
        Assert.Empty(context.Component.FindAll("[data-testid='azure-foundry-settings']"));
    }

    [Fact]
    public void Usage_metrics_and_ai_assistant_settings_coexist_when_enabled()
    {
        using var context = RenderSettings(aiAssistantEnabled: true, usageMetricsEnabled: true);

        Assert.Single(context.Component.FindAll("[data-testid='usage-api-endpoints-settings']"));

        context.Component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "AI").Click();
        context.Component.WaitForAssertion(() => Assert.Single(context.Component.FindAll("[data-testid='azure-foundry-settings']")));

        context.Component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "Features").Click();
        context.Component.WaitForAssertion(() => Assert.Single(context.Component.FindAll("[data-testid='usage-api-endpoints-settings']")));
    }

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
