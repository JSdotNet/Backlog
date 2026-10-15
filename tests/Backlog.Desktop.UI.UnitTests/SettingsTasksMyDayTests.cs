using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.UI.Components.Settings;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Settings › Tasks: the My Day card and its one switch, Clear My Day overnight (local
/// ADR 0032). On by default, saved as it is changed, kept across a restart, and found
/// by the search under its card's title.
/// <para>
/// Most of these render the page on its own; the last render the settings screen to
/// prove the shell carries it at <c>settings/tasks</c> only through the section Tasks
/// registers.
/// </para>
/// </summary>
public sealed class SettingsTasksMyDayTests
{
    private const string Switch = "[data-testid='tasks-my-day-clears-overnight'] button[role='switch']";

    [Fact]
    public void The_page_is_a_product_area_between_the_inbox_and_the_connectors()
    {
        var section = Section();

        Assert.Equal("tasks", section.Id);
        Assert.Equal("Tasks", section.Title);
        Assert.Equal(SettingsGroup.ProductAreas, section.Group);
        Assert.Null(section.FeatureKey);
        Assert.Equal(typeof(TasksSettings), section.Component);
        Assert.Equal(35, section.Order);
        Assert.True(section.Order > InboxSettingsRegistration.Order);
        Assert.True(section.Order < TaskConnectorSettingsRegistration.Order);
    }

    [Fact]
    public void The_switch_is_on_until_it_is_turned_off()
    {
        using var page = RenderSection();

        var toggle = page.Component.Find(Switch);

        Assert.Equal("true", toggle.GetAttribute("aria-checked"));
        Assert.Equal(
            "Clear My Day overnight",
            page.Component.Find($"#{toggle.GetAttribute("aria-labelledby")}").TextContent.Trim());
        Assert.False(File.Exists(page.Path));
    }

    /// <summary>Saved as it is changed, with no button, and read back by the next
    /// launch's store.</summary>
    [Fact]
    public void Turning_the_switch_off_saves_it_and_a_restart_reads_it_back()
    {
        using var page = RenderSection();

        page.Component.Find(Switch).Click();

        Assert.False(page.Preferences.ClearsOvernight);
        page.Component.WaitForAssertion(() =>
            Assert.Equal("false", page.Component.Find(Switch).GetAttribute("aria-checked")));
        Assert.False(new MyDayPreferencesStore(page.Path).ClearsOvernight);
        Assert.Empty(page.Component.FindAll("[data-testid='tasks-my-day-status']"));

        page.Component.Find(Switch).Click();

        Assert.True(page.Preferences.ClearsOvernight);
        Assert.True(new MyDayPreferencesStore(page.Path).ClearsOvernight);
    }

    /// <summary>The switch moved somewhere else — another window, or a copy a sync
    /// pull took — and the page follows it.</summary>
    [Fact]
    public void A_change_made_elsewhere_moves_the_switch()
    {
        using var page = RenderSection();

        Assert.Null(page.Preferences.SetClearsOvernight(false));

        page.Component.WaitForAssertion(() =>
            Assert.Equal("false", page.Component.Find(Switch).GetAttribute("aria-checked")));
    }

    /// <summary>A value that could not be written is still in force for this session,
    /// and the page says it may not survive a restart.</summary>
    [Fact]
    public void A_switch_that_cannot_be_saved_says_so()
    {
        var root = NewRoot();
        var path = System.IO.Path.Combine(root, MyDayPreferencesStore.FileName);

        // A folder where the file should be: reading finds no file, writing fails.
        Directory.CreateDirectory(path);

        using var page = RenderSection(root, path);

        page.Component.Find(Switch).Click();

        Assert.False(page.Preferences.ClearsOvernight);
        Assert.Equal(
            "Changed, but My Day preferences couldn't be saved for next time.",
            page.Component.Find("[data-testid='tasks-my-day-status']").TextContent.Trim());
        Assert.Equal("status", page.Component.Find("[data-testid='tasks-my-day-status']").GetAttribute("role"));
    }

    /// <summary>One card, the one the section declares, holding the switch.</summary>
    [Fact]
    public void The_card_on_the_page_is_the_one_its_section_declares()
    {
        var section = Section();

        using var page = RenderSection();

        Assert.Equal(["My Day"], section.CardTitles!);
        Assert.Equal(section.CardTitles!, page.Component.FindAll(".settings-card__title").Select(title => title.TextContent.Trim()));

        var card = page.Component.Find("section.settings-card[data-testid='tasks-my-day-card']");
        Assert.Equal("tasks-my-day-title", card.GetAttribute("aria-labelledby"));
        Assert.NotNull(card.QuerySelector(Switch));
    }

    [Fact]
    public void The_search_finds_the_page_by_its_my_day_card()
    {
        var match = Assert.Single(SettingsPages.Find([Section()], "my day"));

        Assert.Equal("tasks", match.Page.Id);
        Assert.Equal(["My Day"], match.Cards);
        Assert.Equal("settings/tasks#card-my-day", SettingsPages.CardHref("tasks", "My Day"));
    }

    /// <summary>The settings screen lists Tasks under Product areas between Inbox and
    /// Connectors, opens it at <c>settings/tasks</c>, and its headline follows the
    /// switch in the same render.</summary>
    [Fact]
    public void The_settings_screen_opens_the_page_at_its_address_and_the_headline_follows_the_switch()
    {
        using var settings = RenderSettings();

        var pages = SettingsMenu.Pages(settings.Component);
        Assert.Equal(Array.IndexOf(pages, "Inbox") + 1, Array.IndexOf(pages, "Tasks"));
        Assert.Equal(Array.IndexOf(pages, "Tasks") + 1, Array.IndexOf(pages, "Connectors"));

        SettingsMenu.Open(settings.Component, "Tasks");

        Assert.Equal("My Day starts empty every morning.", Headline(settings.Component));

        settings.Component.Find(Switch).Click();

        Assert.False(settings.Preferences.ClearsOvernight);
        settings.Component.WaitForAssertion(() =>
            Assert.Equal("My Day keeps what you have not finished until you finish it.", Headline(settings.Component)));
    }

    private static SettingsSection Section() =>
        new ServiceCollection().AddTasksSettings()
            .Select(descriptor => descriptor.ImplementationInstance).OfType<SettingsSection>().Single();

    private static string Headline(IRenderedComponent<Settings> component) =>
        component.Find("[data-testid='settings-headline']").TextContent.Trim();

    private static string NewRoot() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "backlog-settings-tasks-my-day-tests", Guid.NewGuid().ToString("n"));

    private static SectionRenderContext RenderSection(string? root = null, string? path = null)
    {
        root ??= NewRoot();
        path ??= System.IO.Path.Combine(root, MyDayPreferencesStore.FileName);

        var preferences = new MyDayPreferencesStore(path);

        var context = new BunitContext();
        context.Services.AddSingleton<IMyDayPreferences>(preferences);

        return new SectionRenderContext(root, path, context, context.Render<TasksSettings>(), preferences);
    }

    private static SettingsRenderContext RenderSettings()
    {
        var root = NewRoot();

        var store = new WorkspaceSettingsStore(System.IO.Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, System.IO.Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);
        _ = features.SetEnabled(AppFeatures.InboxPane, true);

        var githubSettings = new GitHubSettingsStore(System.IO.Path.Combine(root, "github", "github.json"), () => System.IO.Path.Combine(root, "workspace"));
        var preferences = new MyDayPreferencesStore(System.IO.Path.Combine(root, "local", MyDayPreferencesStore.FileName));

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IWorkingHoursSettings>(
            new WorkingHoursSettingsStore(System.IO.Path.Combine(root, "working-hours", "working-hours.json")));
        context.Services.AddSingleton<IUsageResetSettings>(
            new UsageResetSettingsStore(System.IO.Path.Combine(root, "usage-reset", "usage-reset.json")));
        context.Services.AddSingleton<ICaptureSourceSettings>(
            new CaptureSourcesSettingsStore(System.IO.Path.Combine(root, "capture", "capture-sources.json")));
        context.Services.AddSingleton(new AzureFoundrySettingsStore(System.IO.Path.Combine(root, "azure", "azure-foundry.json")));
        context.Services.AddSingleton(new ClaudeSettingsStore(System.IO.Path.Combine(root, "claude", "claude.json")));
        context.Services.AddSingleton(new GitHubIntegration(githubSettings, new NoGitHub(), new NoProbe()));
        context.Services.AddSingleton<FeedbackReporter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(githubSettings, store));
        context.Services.AddSingleton(new DevbookSourceSelection(githubSettings, new StubBranchCatalog()));
        context.Services.AddSingleton<Backlog.Modules.Inbox.Abstractions.Services.IInboxRoutingRules>(
            new InboxRoutingRulesStore(System.IO.Path.Combine(root, "inbox", "inbox-routing-rules.json")));
        context.Services.AddSingleton<IMyDayPreferences>(preferences);
        context.Services.AddInboxSettings();
        context.Services.AddTaskConnectorSettings();
        context.Services.AddTasksSettings();

        return new SettingsRenderContext(root, context, context.Render<Settings>(), preferences);
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
        string Path,
        BunitContext TestContext,
        IRenderedComponent<TasksSettings> Component,
        MyDayPreferencesStore Preferences) : IDisposable
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
        MyDayPreferencesStore Preferences) : IDisposable
    {
        public void Dispose()
        {
            TestContext.Dispose();
            DeleteQuietly(Root);
        }
    }
}
