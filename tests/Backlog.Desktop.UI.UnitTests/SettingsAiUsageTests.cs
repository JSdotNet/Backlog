using Bunit;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class SettingsAiUsageTests
{
    [Fact]
    public void Ai_tab_and_usage_endpoints_are_visible_when_only_usage_metrics_enabled()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        Assert.Contains("AI", SettingsTabs(context.Component));
        Assert.Empty(context.Component.FindAll("[data-testid='claude-usage-settings']"));

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
        {
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']"));
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
        Assert.Contains("Claude usage API settings updated.", context.Component.Find("[data-testid='claude-usage-status']").TextContent);
        Assert.Contains("GitHub usage API settings updated.", context.Component.Find("[data-testid='github-usage-status']").TextContent);
    }

    /// <summary>
    /// Two vendors, two cards. They shared one before, which meant a change to either
    /// endpoint saved both and answered through a status line that could not say which
    /// vendor it meant.
    /// </summary>
    [Fact]
    public void Claude_and_github_have_their_own_cards_and_report_separately()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
        {
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']"));
            Assert.Single(context.Component.FindAll("[data-testid='github-usage-settings']"));
        });

        // The credential fields belong to Claude's card alone.
        var claudeCard = context.Component.Find("[data-testid='claude-usage-settings']");
        Assert.NotNull(claudeCard.QuerySelector("[data-testid='claude-api-key-input']"));

        var gitHubCard = context.Component.Find("[data-testid='github-usage-settings']");
        Assert.Null(gitHubCard.QuerySelector("[data-testid='claude-api-key-input']"));
        Assert.Null(gitHubCard.QuerySelector("[data-testid='claude-usage-endpoint-input']"));

        var claudeInput = context.Component.Find("[data-testid='claude-usage-endpoint-input']");
        claudeInput.Input("https://claude.example.internal");
        claudeInput.Change();

        // Claude's card answered; GitHub's is still saying what it says at rest.
        Assert.Contains("Claude usage API settings updated.", context.Component.Find("[data-testid='claude-usage-status']").TextContent);
        Assert.DoesNotContain("updated", context.Component.Find("[data-testid='github-usage-status']").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The one fact about an Anthropic admin key that cannot be discovered from it:
    /// which actor in the organization is you. Without it the dashboard would have to
    /// show the whole organization's spend under the heading "your usage", so it shows
    /// nothing and says why.
    /// </summary>
    [Fact]
    public void The_claude_account_is_saved_and_the_status_line_says_whose_usage_will_be_reported()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        var status = context.Component.Find("[data-testid='claude-usage-status']");
        Assert.Contains("needs a key", status.TextContent, StringComparison.Ordinal);
        Assert.Contains("needs your Claude account", status.TextContent, StringComparison.Ordinal);

        var actorInput = context.Component.Find("[data-testid='claude-usage-actor-input']");
        actorInput.Input("  person@example.com  ");
        actorInput.Change();

        Assert.Equal("person@example.com", context.ClaudeStore.Current.Actor);
    }

    [Fact]
    public void Clearing_the_claude_account_forgets_it_rather_than_storing_blank_space()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        var actorInput = context.Component.Find("[data-testid='claude-usage-actor-input']");
        actorInput.Input("person@example.com");
        actorInput.Change();

        actorInput = context.Component.Find("[data-testid='claude-usage-actor-input']");
        actorInput.Input("   ");
        actorInput.Change();

        Assert.Null(context.ClaudeStore.Current.Actor);
    }

    /// <summary>
    /// The key is written but never read back into the field. A stored secret that
    /// re-renders into an input is a secret one screenshot away from being shared, so
    /// the placeholder carries the "already set" signal instead.
    /// </summary>
    [Fact]
    public void The_claude_api_key_is_stored_and_never_rendered_back_into_the_field()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        var keyInput = context.Component.Find("[data-testid='claude-api-key-input']");
        Assert.Equal("password", keyInput.GetAttribute("type"));

        keyInput.Input("  sk-ant-admin01-example  ");
        keyInput.Change();

        Assert.Equal("sk-ant-admin01-example", context.ClaudeStore.Current.AdminApiKey);

        keyInput = context.Component.Find("[data-testid='claude-api-key-input']");
        Assert.Equal(string.Empty, keyInput.GetAttribute("value"));
        Assert.Equal("stored - hidden", keyInput.GetAttribute("placeholder"));
    }

    [Fact]
    public void Forgetting_the_claude_api_key_clears_it_and_the_button_waits_until_one_is_stored()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        Assert.True(context.Component.Find("[data-testid='claude-clear-key-button']").HasAttribute("disabled"));

        var keyInput = context.Component.Find("[data-testid='claude-api-key-input']");
        keyInput.Input("sk-ant-admin01-example");
        keyInput.Change();

        context.Component.Find("[data-testid='claude-clear-key-button']").Click();

        Assert.Null(context.ClaudeStore.Current.AdminApiKey);
        Assert.True(context.Component.Find("[data-testid='claude-clear-key-button']").HasAttribute("disabled"));
    }

    /// <summary>
    /// Anthropic also accepts a personal Console key that is not scoped to a workspace,
    /// so a key without the admin prefix is stored and sent. It is worth saying that it
    /// does not look like an admin key, because a workspace-scoped key is the common
    /// mistake and fails with an opaque 401 — but saying it is not the same as refusing.
    /// </summary>
    [Fact]
    public void A_key_without_the_admin_prefix_is_stored_and_the_card_says_what_may_be_wrong()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        var keyInput = context.Component.Find("[data-testid='claude-api-key-input']");
        keyInput.Input("sk-ant-api03-personal-key");
        keyInput.Change();

        Assert.Equal("sk-ant-api03-personal-key", context.ClaudeStore.Current.AdminApiKey);

        var keyId = context.Component.Find("[data-testid='claude-api-key-input']").GetAttribute("id");
        var hint = context.Component.Find($"#{keyId}-help").TextContent;
        Assert.Contains("workspace", hint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("still be sent", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_workspace_filter_is_saved_and_blank_forgets_it()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        var workspaceInput = context.Component.Find("[data-testid='claude-workspace-input']");
        workspaceInput.Input("  wrkspc_01  ");
        workspaceInput.Change();

        Assert.Equal("wrkspc_01", context.ClaudeStore.Current.WorkspaceId);

        workspaceInput = context.Component.Find("[data-testid='claude-workspace-input']");
        workspaceInput.Input("   ");
        workspaceInput.Change();

        Assert.Null(context.ClaudeStore.Current.WorkspaceId);
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
            Assert.Empty(context.Component.FindAll("[data-testid='claude-usage-settings']"));
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
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']"));
        });
    }

    private static string[] SettingsTabs(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Select(button => button.TextContent.Trim()).ToArray();

    private static void OpenAiTab(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "AI").Click();

    private static SettingsRenderContext RenderSettings(bool aiAssistantEnabled, bool usageMetricsEnabled)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, aiAssistantEnabled);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, usageMetricsEnabled);

        var azureFoundry = new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json"));
        var claude = new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json"));
        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, _) = GitHubSettings.ParseText("JSdotNet/Backlog");
        _ = githubSettings.SetRepositories(repositories);

        var github = new GitHubIntegration(githubSettings, new StubGitHubClient(), new StubProbe());

        var testContext = new BunitContext();
        testContext.Services.AddSingleton(store);
        testContext.Services.AddSingleton<IAppFeatureSettings>(features);
        testContext.Services.AddSingleton<ITasksRefreshSettings>(
            new TasksRefreshSettingsStore(Path.Combine(root, "refresh", "refresh.json")));
        testContext.Services.AddSingleton(azureFoundry);
        testContext.Services.AddSingleton(claude);
        testContext.Services.AddSingleton(github);
        testContext.Services.AddSingleton<FeedbackReporter>();
        testContext.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        testContext.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(githubSettings, store));

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

        public Task<GitHubUploadedFile> UploadFileAsync(
            GitHubRepositoryRef repository,
            string path,
            string branch,
            byte[] content,
            string commitMessage,
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
