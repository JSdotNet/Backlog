using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Capture tab: which sources are watched and what they are pointed at.
/// <para>
/// Asserted against the store rather than against what the rows are showing,
/// because the store is what a run reads: a toggle that flipped and wrote
/// nothing would leave the next run looking at exactly what it looked at
/// before. Committing on change with no save button is the house rule the rest
/// of this screen follows.
/// </para>
/// </summary>
public sealed class SettingsCaptureSourcesTests
{
    /// <summary>The tab hangs off the Inbox pane's own feature: there is no
    /// Capture button to press without the pane it lives on.</summary>
    [Fact]
    public void The_tab_is_hidden_while_the_inbox_pane_is_off()
    {
        using var settings = RenderSettings(inboxPane: false);

        Assert.DoesNotContain(
            settings.Component.FindAll(".settings-tabs button"),
            button => button.TextContent.Trim() == "Capture");
    }

    [Fact]
    public void The_tab_is_shown_when_the_inbox_pane_is_on()
    {
        using var settings = RenderSettings();
        OpenCaptureTab(settings.Component);

        Assert.NotEmpty(settings.Component.FindAll("[data-testid='capture-settings']"));
    }

    [Fact]
    public void Every_monitorable_source_gets_a_toggle_and_a_targets_field()
    {
        using var settings = RenderSettings();
        OpenCaptureTab(settings.Component);

        foreach (var kind in CaptureSourceKinds.Monitorable)
        {
            var slug = CaptureSourceKinds.Slug(kind);

            var block = settings.Component.Find($"[data-testid='capture-source-{slug}']");
            Assert.Contains(CaptureSourceKinds.Label(kind), block.TextContent, StringComparison.Ordinal);
            Assert.NotEmpty(settings.Component.FindAll($"[data-testid='capture-source-{slug}-enabled']"));
            Assert.NotEmpty(settings.Component.FindAll($"[data-testid='capture-source-{slug}-targets']"));
        }
    }

    [Fact]
    public void Toggling_a_source_is_stored_straight_away()
    {
        using var settings = RenderSettings();
        OpenCaptureTab(settings.Component);

        settings.Component.Find("[data-testid='capture-source-youtube-enabled'] input").Change(true);

        Assert.True(settings.CaptureSources.Current.For(CaptureSourceKind.YouTube).Enabled);
        Assert.False(settings.CaptureSources.Current.For(CaptureSourceKind.Website).Enabled);
        Assert.DoesNotContain(
            "setting__status--error",
            settings.Component.Find("[data-testid='capture-source-youtube-status']").InnerHtml);
    }

    /// <summary>One target per line, the way the Repositories tab takes its
    /// list. Blank lines are not targets.</summary>
    [Fact]
    public void Committed_targets_are_stored_one_per_line()
    {
        using var settings = RenderSettings();
        OpenCaptureTab(settings.Component);

        var field = settings.Component.Find("[data-testid='capture-source-website-targets']");
        field.Input("https://example.com/blog\n\n  https://example.org/changelog  \n");
        field.Change("https://example.com/blog\n\n  https://example.org/changelog  \n");

        Assert.Equal(
            ["https://example.com/blog", "https://example.org/changelog"],
            settings.CaptureSources.Current.For(CaptureSourceKind.Website).Targets);
    }

    [Fact]
    public void The_field_opens_showing_what_is_in_force()
    {
        using var settings = RenderSettings(
            before: store => store.SetTargets(CaptureSourceKind.Email, ["news@example.com", "digest@example.org"]));
        OpenCaptureTab(settings.Component);

        var field = settings.Component.Find("[data-testid='capture-source-email-targets']");
        Assert.Equal("news@example.com\ndigest@example.org", field.TextContent);
    }

    /// <summary>The store's refusal is the row's status line, the same shape as
    /// every other setting on this screen.</summary>
    [Fact]
    public void A_store_error_is_shown_on_the_status_line()
    {
        using var settings = RenderSettings(captureSources: new RefusingCaptureSources());
        OpenCaptureTab(settings.Component);

        settings.Component.Find("[data-testid='capture-source-email-enabled'] input").Change(true);

        var status = settings.Component.Find("[data-testid='capture-source-email-status']");
        Assert.Contains("setting__status--error", status.InnerHtml);
        Assert.Contains("couldn't be saved", status.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tab_says_where_the_choices_are_kept()
    {
        using var settings = RenderSettings();
        OpenCaptureTab(settings.Component);

        Assert.Contains(
            settings.CaptureSources.SettingsPath,
            settings.Component.Find("[data-testid='capture-settings']").TextContent,
            StringComparison.Ordinal);
    }

    private static void OpenCaptureTab(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "Capture").Click();

    private static SettingsRenderContext RenderSettings(
        bool inboxPane = true,
        ICaptureSourceSettings? captureSources = null,
        Action<ICaptureSourceSettings>? before = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-capture-sources-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);
        _ = features.SetEnabled(AppFeatures.InboxPane, inboxPane);

        captureSources ??= new CaptureSourcesSettingsStore(Path.Combine(root, "capture", "capture-sources.json"));
        // Before the render: the page seeds its drafts once, when it initializes.
        before?.Invoke(captureSources);
        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<ITasksRefreshSettings>(
            new TasksRefreshSettingsStore(Path.Combine(root, "refresh", "refresh.json")));
        context.Services.AddSingleton<IWorkingHoursSettings>(
            new WorkingHoursSettingsStore(Path.Combine(root, "working-hours", "working-hours.json")));
        context.Services.AddSingleton(captureSources);
        context.Services.AddSingleton(new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json")));
        context.Services.AddSingleton(new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json")));
        context.Services.AddSingleton(new GitHubIntegration(githubSettings, new NoGitHub(), new NoProbe()));
        context.Services.AddSingleton<FeedbackReporter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(githubSettings, store));
        context.Services.AddSingleton(new KnowledgeSourceSelection(githubSettings, new StubBranchCatalog()));

        return new SettingsRenderContext(root, context, context.Render<Settings>(), captureSources);
    }

    /// <summary>A store whose disk has gone away: every write is refused with
    /// the message the real one gives, and nothing changes.</summary>
    private sealed class RefusingCaptureSources : ICaptureSourceSettings
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public CaptureSourceSettings Current { get; } = new();

        public string SettingsPath => Path.Combine("nowhere", "capture-sources.json");

        public string? SetEnabled(CaptureSourceKind kind, bool enabled) =>
            "Changed, but the capture sources couldn't be saved for next time.";

        public string? SetTargets(CaptureSourceKind kind, IReadOnlyList<string> targets) =>
            "Changed, but the capture sources couldn't be saved for next time.";
    }

    private sealed class NoGitHub : IGitHubClient
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
        ICaptureSourceSettings CaptureSources) : IDisposable
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
