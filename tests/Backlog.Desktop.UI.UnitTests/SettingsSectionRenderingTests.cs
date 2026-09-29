using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Capture.Abstractions.Services;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The settings sections a module registers, drawn by the shell after its own
/// app-wide pages. The shell knows each one only as a <see cref="SettingsSection"/>
/// — a title, a place in the order and a component to render — so these use
/// stand-in components rather than any module's real one.
/// </summary>
public sealed class SettingsSectionRenderingTests
{
    private static readonly string[] AppWidePages = ["Features", "Storage", "Repositories"];

    [Fact]
    public void With_no_section_registered_only_the_app_wide_pages_are_offered()
    {
        using var settings = RenderSettings();

        Assert.Equal(AppWidePages, Tabs(settings.Component));
    }

    [Fact]
    public void Registered_sections_follow_the_app_wide_pages_in_ascending_order()
    {
        using var settings = RenderSettings(
            new SettingsSection("later", "Later", 20, typeof(LaterSection)),
            new SettingsSection("sooner", "Sooner", 10, typeof(SoonerSection)));

        Assert.Equal([.. AppWidePages, "Sooner", "Later"], Tabs(settings.Component));
    }

    [Fact]
    public void Opening_a_section_renders_its_component_under_its_title()
    {
        using var settings = RenderSettings(new SettingsSection("sooner", "Sooner", 10, typeof(SoonerSection)));
        Assert.Empty(settings.Component.FindAll("[data-testid='sooner-section']"));

        OpenTab(settings.Component, "Sooner");

        var panel = settings.Component.Find("section[aria-label='Sooner settings']");
        Assert.Equal("Sooner", panel.QuerySelector("h2.setting__title")!.TextContent.Trim());
        Assert.NotNull(panel.QuerySelector("[data-testid='sooner-section']"));
    }

    [Fact]
    public void A_section_behind_a_feature_that_is_off_is_not_offered()
    {
        using var settings = RenderSettings(
            new SettingsSection("sooner", "Sooner", 10, typeof(SoonerSection), FeatureKey: AppFeatures.InboxPane));

        Assert.Equal(AppWidePages, Tabs(settings.Component));
    }

    [Fact]
    public void Switching_a_sections_feature_off_withdraws_its_page()
    {
        using var settings = RenderSettings(
            new SettingsSection("sooner", "Sooner", 10, typeof(SoonerSection), FeatureKey: AppFeatures.InboxPane),
            inboxPane: true);
        OpenTab(settings.Component, "Sooner");

        OpenTab(settings.Component, "Features");
        settings.Component.FindAll(".feature-flag")
            .Single(row => row.QuerySelector(".feature-flag__title")?.TextContent == "Inbox pane")
            .QuerySelector("input")!
            .Change(false);

        Assert.Equal(AppWidePages, Tabs(settings.Component));
        Assert.Equal("true", settings.Component.FindAll(".settings-tabs button").Single(tab => tab.TextContent.Trim() == "Features").GetAttribute("aria-selected"));
    }

    public sealed class SoonerSection : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddAttribute(1, "data-testid", "sooner-section");
            builder.AddContent(2, "Sooner");
            builder.CloseElement();
        }
    }

    public sealed class LaterSection : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddAttribute(1, "data-testid", "later-section");
            builder.AddContent(2, "Later");
            builder.CloseElement();
        }
    }

    private static string[] Tabs(IRenderedComponent<Settings> component) =>
        [.. component.FindAll(".settings-tabs button").Select(button => button.TextContent.Trim())];

    private static void OpenTab(IRenderedComponent<Settings> component, string title) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == title).Click();

    private static SettingsRenderContext RenderSettings(params SettingsSection[] sections) =>
        RenderSettings(sections, inboxPane: false);

    private static SettingsRenderContext RenderSettings(SettingsSection section, bool inboxPane) =>
        RenderSettings([section], inboxPane);

    private static SettingsRenderContext RenderSettings(SettingsSection[] sections, bool inboxPane)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-section-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);
        _ = features.SetEnabled(AppFeatures.InboxPane, inboxPane);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"), () => Path.Combine(root, "workspace"));

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
        foreach (var section in sections) context.Services.AddSingleton(section);

        return new SettingsRenderContext(root, context, context.Render<Settings>());
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
        IRenderedComponent<Settings> Component) : IDisposable
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
