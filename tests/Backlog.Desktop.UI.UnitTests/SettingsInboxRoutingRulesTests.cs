using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Inbox routing rules on the Repositories page: drawn only while the Inbox
/// pane is on and the host keeps rules somewhere, kept when every line reads,
/// and refused whole — with the line named — when one does not.
/// </summary>
public sealed class SettingsInboxRoutingRulesTests
{
    [Fact]
    public void Rules_typed_into_the_field_are_kept_and_counted()
    {
        using var settings = RenderSettings();
        OpenRepositoriesTab(settings.Component);

        Type(settings.Component, "#design => JSdotNet/Design\n\n@alice => JSdotNet/Backlog");

        Assert.Equal(
            [new InboxRoutingRule("#design", "JSdotNet/Design"), new InboxRoutingRule("@alice", "JSdotNet/Backlog")],
            settings.Rules.Current);
        Assert.Equal("2 rules are in use.", settings.Component.Find("[data-testid='inbox-routing-rules-result']").TextContent.Trim());
    }

    [Fact]
    public void A_line_that_does_not_read_is_named_and_the_rules_in_use_stay()
    {
        using var settings = RenderSettings();
        Assert.Null(settings.Rules.SetRules("#design => JSdotNet/Design"));
        OpenRepositoriesTab(settings.Component);

        Type(settings.Component, "#design => JSdotNet/Design\nyoutube JSdotNet/Watch");

        Assert.Equal([new InboxRoutingRule("#design", "JSdotNet/Design")], settings.Rules.Current);
        Assert.StartsWith(
            "Line 2 has no \"=>\"",
            settings.Component.Find("[data-testid='inbox-routing-rules-result']").TextContent.Trim(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_field_opens_with_the_rules_already_in_use()
    {
        using var settings = RenderSettings(existing: "youtube => JSdotNet/Watch");
        OpenRepositoriesTab(settings.Component);

        Assert.Equal("youtube => JSdotNet/Watch", settings.Component.Find("[data-testid='inbox-routing-rules-input']").GetAttribute("value"));
        Assert.Equal("1 rule is in use.", settings.Component.Find("[data-testid='inbox-routing-rules-result']").TextContent.Trim());
    }

    [Fact]
    public void Without_the_inbox_pane_the_field_is_not_drawn()
    {
        using var settings = RenderSettings(inboxPane: false);
        OpenRepositoriesTab(settings.Component);

        Assert.Empty(settings.Component.FindAll("[data-testid='inbox-routing-rules-input']"));
    }

    /// <summary>What typing does: an input per keystroke, then the change the
    /// field commits on when it loses focus.</summary>
    private static void Type(IRenderedComponent<Settings> component, string text)
    {
        component.Find("[data-testid='inbox-routing-rules-input']").Input(text);
        component.Find("[data-testid='inbox-routing-rules-input']").Change(text);
    }

    private static void OpenRepositoriesTab(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "Repositories").Click();

    private static SettingsRenderContext RenderSettings(bool inboxPane = true, string? existing = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-routing-rules-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);
        _ = features.SetEnabled(AppFeatures.InboxPane, inboxPane);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"), () => Path.Combine(root, "workspace"));
        var (repositories, errors) = GitHubSettings.ParseText("backlog = JSdotNet/Backlog\ndesign = JSdotNet/Design");
        Assert.Empty(errors);
        Assert.Null(githubSettings.SetRepositories(repositories));

        var rules = new InboxRoutingRulesStore(Path.Combine(root, "inbox", "inbox-routing-rules.json"));
        if (existing is not null) Assert.Null(rules.SetRules(existing));

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IWorkingHoursSettings>(
            new WorkingHoursSettingsStore(Path.Combine(root, "working-hours", "working-hours.json")));
        context.Services.AddSingleton<IUsageResetSettings>(
            new UsageResetSettingsStore(Path.Combine(root, "usage-reset", "usage-reset.json")));
        context.Services.AddSingleton<ICaptureSourceSettings>(
            new CaptureSourcesSettingsStore(Path.Combine(root, "capture", "capture-sources.json")));
        context.Services.AddSingleton(new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json")));
        context.Services.AddSingleton(new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json")));
        context.Services.AddSingleton(new GitHubIntegration(githubSettings, new NoGitHub(), new NoProbe()));
        context.Services.AddSingleton<FeedbackReporter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(githubSettings, store));
        context.Services.AddSingleton(new DevbookSourceSelection(githubSettings, new StubBranchCatalog()));
        context.Services.AddSingleton<IInboxRoutingRules>(rules);

        return new SettingsRenderContext(root, context, context.Render<Settings>(), rules);
    }

    private sealed class NoGitHub : IGitHubClient
    {
        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssue> CreateIssueAsync(GitHubRepositoryRef repository, string title, string? body, IEnumerable<string>? labels = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(GitHubRepositoryRef repository, string path, string branch, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not connected."));

        public void Invalidate()
        {
        }
    }

    private sealed record SettingsRenderContext(
        string Root,
        BunitContext TestContext,
        IRenderedComponent<Settings> Component,
        InboxRoutingRulesStore Rules) : IDisposable
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
}
