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
/// The Inbox routing rules, on the Inbox's own settings section: drawn only while
/// the Inbox pane is on and the host keeps rules somewhere, kept when every line
/// reads, and refused whole — with the line named — when one does not.
/// <para>
/// The section is the Inbox's component, so most of these render it on its own;
/// the last two render the settings screen to prove the shell carries it only
/// through the section the Inbox registers, and holds no rules field of its own.
/// </para>
/// </summary>
public sealed class SettingsInboxRoutingRulesTests
{
    [Fact]
    public void Rules_typed_into_the_field_are_kept_and_counted()
    {
        using var section = RenderSection();

        Type(section.Component, "#design => JSdotNet/Design\n\n@alice => JSdotNet/Backlog");

        Assert.Equal(
            [new InboxRoutingRule("#design", "JSdotNet/Design"), new InboxRoutingRule("@alice", "JSdotNet/Backlog")],
            section.Rules.Current);
        Assert.Equal("2 rules are in use.", section.Component.Find("[data-testid='inbox-routing-rules-result']").TextContent.Trim());
    }

    [Fact]
    public void A_line_that_does_not_read_is_named_and_the_rules_in_use_stay()
    {
        using var section = RenderSection(existing: "#design => JSdotNet/Design");

        Type(section.Component, "#design => JSdotNet/Design\nyoutube JSdotNet/Watch");

        Assert.Equal([new InboxRoutingRule("#design", "JSdotNet/Design")], section.Rules.Current);
        Assert.StartsWith(
            "Line 2 has no \"=>\"",
            section.Component.Find("[data-testid='inbox-routing-rules-result']").TextContent.Trim(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_field_opens_with_the_rules_already_in_use()
    {
        using var section = RenderSection(existing: "youtube => JSdotNet/Watch");

        Assert.Equal("youtube => JSdotNet/Watch", section.Component.Find("[data-testid='inbox-routing-rules-input']").GetAttribute("value"));
        Assert.Equal("1 rule is in use.", section.Component.Find("[data-testid='inbox-routing-rules-result']").TextContent.Trim());
    }

    [Fact]
    public void The_field_is_described_by_the_line_under_it()
    {
        using var section = RenderSection();

        Assert.Equal(
            "inbox-routing-rules-result",
            section.Component.Find("[data-testid='inbox-routing-rules-input']").GetAttribute("aria-describedby"));
        Assert.Equal("inbox-routing-rules-result", section.Component.Find("[data-testid='inbox-routing-rules-result']").GetAttribute("id"));
    }

    [Fact]
    public void Without_the_inbox_pane_the_field_is_not_drawn()
    {
        using var section = RenderSection(inboxPane: false);

        Assert.Empty(section.Component.FindAll("[data-testid='inbox-routing-rules-input']"));
    }

    [Fact]
    public void Without_a_rules_store_the_field_is_not_drawn()
    {
        using var section = RenderSection(withRules: false);

        Assert.Empty(section.Component.FindAll("[data-testid='inbox-routing-rules-input']"));
    }

    [Fact]
    public void The_settings_screen_draws_the_rules_only_on_the_page_the_inbox_registers()
    {
        using var settings = RenderSettings(registerSection: true);

        OpenTab(settings.Component, "Repositories");
        Assert.Empty(settings.Component.FindAll("[data-testid='inbox-routing-rules-input']"));

        OpenTab(settings.Component, "Inbox");
        Type(settings.Component, "#design => JSdotNet/Design");

        Assert.Equal([new InboxRoutingRule("#design", "JSdotNet/Design")], settings.Rules.Current);
    }

    [Fact]
    public void Without_the_inbox_section_the_settings_screen_has_no_rules_field()
    {
        using var settings = RenderSettings(registerSection: false);

        foreach (var tab in Tabs(settings.Component))
        {
            OpenTab(settings.Component, tab);
            Assert.Empty(settings.Component.FindAll("[data-testid='inbox-routing-rules-input']"));
        }
    }

    /// <summary>What typing does: an input per keystroke, then the change the
    /// field commits on when it loses focus.</summary>
    private static void Type<TComponent>(IRenderedComponent<TComponent> component, string text)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        component.Find("[data-testid='inbox-routing-rules-input']").Input(text);
        component.Find("[data-testid='inbox-routing-rules-input']").Change(text);
    }

    private static string[] Tabs(IRenderedComponent<Settings> component) =>
        [.. component.FindAll(".settings-tabs button").Select(button => button.TextContent.Trim())];

    private static void OpenTab(IRenderedComponent<Settings> component, string title) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == title).Click();

    private static SectionRenderContext RenderSection(bool inboxPane = true, bool withRules = true, string? existing = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-routing-rules-tests", Guid.NewGuid().ToString("n"));

        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(AppFeatures.InboxPane, inboxPane);

        var rules = new InboxRoutingRulesStore(Path.Combine(root, "inbox", "inbox-routing-rules.json"));
        if (existing is not null) Assert.Null(rules.SetRules(existing));

        var context = new BunitContext();
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        if (withRules) context.Services.AddSingleton<IInboxRoutingRules>(rules);

        return new SectionRenderContext(root, context, context.Render<InboxSettings>(), rules);
    }

    private static SettingsRenderContext RenderSettings(bool registerSection)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-routing-rules-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);
        _ = features.SetEnabled(AppFeatures.InboxPane, true);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"), () => Path.Combine(root, "workspace"));
        var (repositories, errors) = GitHubSettings.ParseText("backlog = JSdotNet/Backlog\ndesign = JSdotNet/Design");
        Assert.Empty(errors);
        Assert.Null(githubSettings.SetRepositories(repositories));

        var rules = new InboxRoutingRulesStore(Path.Combine(root, "inbox", "inbox-routing-rules.json"));

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
        if (registerSection) context.Services.AddInboxSettings();

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

    private static void DeleteQuietly(string root)
    {
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed record SectionRenderContext(
        string Root,
        BunitContext TestContext,
        IRenderedComponent<InboxSettings> Component,
        InboxRoutingRulesStore Rules) : IDisposable
    {
        public void Dispose()
        {
            TestContext.Dispose();
            DeleteQuietly(Root);
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
            DeleteQuietly(Root);
        }
    }
}
