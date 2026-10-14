using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.UI.Components.Settings;

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
    internal static readonly string[] AppWidePages = ["Features", "Storage & backup", "Repositories"];

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
        Assert.Equal("Sooner", settings.Component.Find("h1").TextContent.Trim());
        Assert.Null(panel.QuerySelector("h2"));
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
        Assert.Equal("Features", SettingsMenu.Current(settings.Component));
    }

    [Fact]
    public void Settings_with_no_page_in_its_address_opens_Features()
    {
        using var settings = RenderSettings();

        Assert.Equal("Features", SettingsMenu.Current(settings.Component));
        Assert.EndsWith("of them still unfinished.", Headline(settings.Component));
        Assert.Equal("Settings · Workspace", settings.Component.Find("[data-testid='settings-eyebrow']").TextContent.Trim());
    }

    [Fact]
    public void An_address_naming_no_page_opens_Features()
    {
        using var settings = RenderSettings();
        SettingsMenu.Navigate(settings.Component, "storage");

        SettingsMenu.Navigate(settings.Component, "no-such-page");

        Assert.Equal("Features", SettingsMenu.Current(settings.Component));
        Assert.False(settings.Component.Find("section[aria-label='Feature settings']").HasAttribute("hidden"));
    }

    [Fact]
    public void Every_page_in_the_menu_links_to_its_own_address()
    {
        using var settings = RenderSettings(new SettingsSection("sooner", "Sooner", 10, typeof(SoonerSection)));

        var hrefs = settings.Component.FindAll("nav[aria-label='Settings pages'] a")
            .Select(link => (Page: link.GetAttribute("data-page"), Href: link.GetAttribute("href")))
            .ToList();

        Assert.Equal(
            [("features", "settings/features"), ("storage", "settings/storage"), ("repositories", "settings/repositories"), ("sooner", "settings/sooner")],
            hrefs);
    }

    [Fact]
    public void An_address_opens_its_page_and_keeps_the_page_before_alive_hidden()
    {
        using var settings = RenderSettings(new SettingsSection("sooner", "Sooner", 10, typeof(SoonerSection)));

        SettingsMenu.Navigate(settings.Component, "sooner");

        Assert.Equal("Sooner", SettingsMenu.Current(settings.Component));
        Assert.Equal("page", settings.Component.Find("a[data-page='sooner']").GetAttribute("aria-current"));
        Assert.Null(settings.Component.Find("a[data-page='features']").GetAttribute("aria-current"));
        Assert.False(settings.Component.Find("section[aria-label='Sooner settings']").HasAttribute("hidden"));
        Assert.True(settings.Component.Find("section[aria-label='Feature settings']").HasAttribute("hidden"));
        Assert.Equal("Settings · Product areas", settings.Component.Find("[data-testid='settings-eyebrow']").TextContent.Trim());
    }

    [Fact]
    public void An_older_query_link_still_opens_the_page_it_names()
    {
        using var settings = RenderSettings(
            [new SettingsSection("sooner", "Sooner", 10, typeof(SoonerSection))],
            inboxPane: false,
            navigateTo: "settings?page=sooner");

        Assert.Equal("Sooner", SettingsMenu.Current(settings.Component));
    }

    [Fact]
    public void The_menu_groups_built_in_pages_first_then_registered_ones_by_order()
    {
        using var settings = RenderSettings(
            new SettingsSection("late-area", "Late area", 20, typeof(LaterSection), Group: SettingsGroup.ProductAreas),
            new SettingsSection("early-area", "Early area", 10, typeof(SoonerSection), Group: SettingsGroup.ProductAreas),
            new SettingsSection("pricing", "Pricing", 1, typeof(SoonerSection), Group: SettingsGroup.Ai),
            new SettingsSection("workspace-extra", "Workspace extra", 5, typeof(SoonerSection), Group: SettingsGroup.Workspace));

        Assert.Equal(
            ["Workspace", "AI", "Product areas"],
            settings.Component.FindAll(".settings-menu-group__label").Select(label => label.TextContent.Trim()));
        Assert.Equal(
            ["Features", "Storage & backup", "Repositories", "Workspace extra", "Pricing", "Early area", "Late area"],
            SettingsMenu.Pages(settings.Component));
    }

    [Fact]
    public void A_page_supplies_its_own_headline_sentence_while_it_is_on_screen()
    {
        using var settings = RenderSettings(new SettingsSection("worded", "Worded", 10, typeof(WordedSection)));

        SettingsMenu.Open(settings.Component, "Worded");
        Assert.Equal("Say what this page is for in one sentence.", Headline(settings.Component));

        // Kept alive and hidden, the page no longer writes the headline.
        SettingsMenu.Open(settings.Component, "Features");
        Assert.EndsWith("of them still unfinished.", Headline(settings.Component));

        SettingsMenu.Open(settings.Component, "Worded");
        Assert.Equal("Say what this page is for in one sentence.", Headline(settings.Component));
    }

    [Fact]
    public void The_product_menu_lists_every_page_in_its_group_and_order()
    {
        var services = new ServiceCollection();
        services.AddTaskConnectorSettings();
        services.AddInboxSettings();
        Backlog.Modules.Sessions.UI.Extensions.SessionsSettingsRegistration.AddSessionsSettings(services);
        Backlog.Modules.Sync.UI.SyncSettingsRegistration.AddSyncSettings(services);
        Backlog.Modules.Dashboard.UI.Extensions.DashboardSettingsRegistration.AddDashboardSettings(services);
        var registered = services.Select(descriptor => descriptor.ImplementationInstance).OfType<SettingsSection>();

        var pages = SettingsPages.InMenuOrder(SettingsPages.BuiltIn, registered);

        Assert.Equal(
            [
                (SettingsGroup.Workspace, "Features"),
                (SettingsGroup.Workspace, "Storage & backup"),
                (SettingsGroup.Workspace, "Repositories"),
                (SettingsGroup.Workspace, "GitHub accounts"),
                (SettingsGroup.Ai, "AI providers"),
                (SettingsGroup.Ai, "Claude Code"),
                (SettingsGroup.Ai, "Model prices"),
                (SettingsGroup.ProductAreas, "Dashboard"),
                (SettingsGroup.ProductAreas, "Working week"),
                (SettingsGroup.ProductAreas, "Inbox"),
                (SettingsGroup.ProductAreas, "Connectors"),
                (SettingsGroup.ProductAreas, "Devices"),
            ],
            pages.Select(page => (page.Group, page.Title)));
    }

    [Fact]
    public void Every_built_in_page_declares_the_cards_it_draws()
    {
        Assert.All(SettingsPages.BuiltIn, page => Assert.NotNull(page.CardTitles));
        Assert.Equal(["Product areas", "Cross-cutting"], SettingsPages.BuiltIn.Single(page => page.Id == SettingsPages.Features).CardTitles!);
    }

    [Fact]
    public void The_header_says_settings_save_as_you_go_and_keeps_a_way_back()
    {
        using var settings = RenderSettings();

        Assert.Contains("Saved as you go", settings.Component.Find("[data-testid='settings-saved']").TextContent);
        var back = settings.Component.Find("[data-testid='back-link']");
        Assert.Equal("Back to your tasks", back.GetAttribute("aria-label"));
        Assert.Equal("", back.GetAttribute("href"));
        Assert.NotNull(settings.Component.Find("[data-testid='settings-search-slot']"));
    }

    // Which part shows at phone width is the frame's container query; the screen's
    // part is telling the frame whether the address names a page, which these pin.

    [Fact]
    public void Settings_with_no_page_in_its_address_is_the_grouped_list_at_phone_width()
    {
        using var settings = RenderSettings();

        var frame = settings.Component.Find("[data-testid='settings-frame']");
        Assert.Contains("settings-frame--index", frame.ClassList);
        Assert.Empty(settings.Component.FindAll("[data-testid='back-to-menu-link']"));

        // The headline over the list is the Features page's sentence, which
        // /settings opens: the page is hidden at phone width but still on screen
        // as far as the cascade goes, so it writes the headline.
        Assert.EndsWith("of them still unfinished.", Headline(settings.Component));
    }

    [Fact]
    public void An_address_naming_a_page_shows_the_page_alone_at_phone_width_with_a_way_back_to_the_list()
    {
        using var settings = RenderSettings();

        SettingsMenu.Open(settings.Component, "Storage & backup");

        Assert.Contains("settings-frame--page", settings.Component.Find("[data-testid='settings-frame']").ClassList);
        Assert.Equal("settings", settings.Component.Find("[data-testid='back-to-menu-link']").GetAttribute("href"));

        SettingsMenu.Navigate(settings.Component, null);

        Assert.Contains("settings-frame--index", settings.Component.Find("[data-testid='settings-frame']").ClassList);
        Assert.Empty(settings.Component.FindAll("[data-testid='back-to-menu-link']"));
    }

    [Fact]
    public void An_older_query_link_names_a_page_too()
    {
        using var settings = RenderSettings([], inboxPane: false, navigateTo: "settings?page=storage");

        Assert.Contains("settings-frame--page", settings.Component.Find("[data-testid='settings-frame']").ClassList);
    }

    private static string Headline(IRenderedComponent<Settings> component) =>
        component.Find("h1").TextContent.Trim();

    public sealed class WordedSection : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<SettingsHeadline>(0);
            builder.AddAttribute(1, nameof(SettingsHeadline.ChildContent), (RenderFragment)(sentence => sentence.AddContent(0, "Say what this page is for in one sentence.")));
            builder.CloseComponent();
        }
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
        SettingsMenu.Pages(component);

    private static void OpenTab(IRenderedComponent<Settings> component, string title) =>
        SettingsMenu.Open(component, title);

    internal static SettingsRenderContext RenderSettings(params SettingsSection[] sections) =>
        RenderSettings(sections, inboxPane: false);

    private static SettingsRenderContext RenderSettings(SettingsSection section, bool inboxPane) =>
        RenderSettings([section], inboxPane);

    private static SettingsRenderContext RenderSettings(SettingsSection[] sections, bool inboxPane, string? navigateTo = null)
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

        if (navigateTo is not null) context.Services.GetRequiredService<NavigationManager>().NavigateTo(navigateTo);

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

    internal sealed record SettingsRenderContext(
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
