using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Brain card on Settings › Storage (<c>.devbook/domain/devbook/requirements.md#brain-folder-setup</c>):
/// an empty field on a fresh install, the path kept in the app's settings and not
/// the folder, the database line, Create conventions scaffolding without
/// overwriting, and a link that opens the screen at the card.
/// </summary>
public sealed class SettingsBrainFolderTests
{
    private const string Card = "[data-testid='brain-folder-card']";
    private const string Field = "[data-testid='brain-folder-card-folder']";

    /// <summary>The requirement's "A fresh install" scenario.</summary>
    [Fact]
    public void A_fresh_install_shows_the_brain_card_with_an_empty_field()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        Assert.Equal(string.Empty, settings.Component.Find(Field).GetAttribute("value") ?? string.Empty);
        Assert.Contains("never synced", settings.Component.Find("[data-testid='brain-folder-card-database']").TextContent, StringComparison.Ordinal);
        Assert.Empty(settings.Component.FindAll("[data-testid='brain-folder-card-create-conventions']"));
    }

    /// <summary>The requirement's "Picking a folder" scenario: the app's settings
    /// hold the path and the folder is still empty.</summary>
    [Fact]
    public void Picking_a_folder_keeps_its_path_in_the_app_settings_and_the_folder_empty()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);
        var brain = Path.Combine(settings.Root, "Brain");
        Directory.CreateDirectory(brain);

        settings.Component.Find(Field).Change(brain);

        Assert.Equal(brain, settings.Brain.BrainFolder);
        Assert.Contains("Brain", File.ReadAllText(settings.Brain.SettingsPath), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(brain));
        Assert.Contains("missing", settings.Component.Find("[data-testid='brain-folder-card-conventions']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>The requirement's "A OneDrive folder" scenario.</summary>
    [Fact]
    public void A_onedrive_folder_is_said_to_be_synced_without_a_warning()
    {
        using var settings = RenderSettings(_ => new SyncedFolderMatch("OneDrive", @"C:\Users\sam\OneDrive"));
        OpenStorageTab(settings.Component);

        settings.Component.Find(Field).Change(Path.Combine(settings.Root, "OneDrive", "Brain"));

        var card = settings.Component.Find(Card);
        Assert.Contains("Synced by OneDrive", card.TextContent, StringComparison.Ordinal);
        Assert.Contains("built in this app's storage and never synced", card.TextContent, StringComparison.Ordinal);
        Assert.Empty(card.QuerySelectorAll("[role='alert']"));
    }

    /// <summary>The requirement's "An empty folder" scenario, through the
    /// button.</summary>
    [Fact]
    public void Create_conventions_scaffolds_the_folder_and_then_goes_away()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);
        var brain = Path.Combine(settings.Root, "Brain");
        settings.Component.Find(Field).Change(brain);

        settings.Component.Find("[data-testid='brain-folder-card-create-conventions']").Click();

        Assert.True(File.Exists(Path.Combine(brain, "README.md")));
        Assert.True(File.Exists(Path.Combine(brain, "CONVENTIONS.md")));
        Assert.All(
            ["_inbox", "projects", "areas", "resources", "people", "journal", "archive"],
            folder => Assert.True(Directory.Exists(Path.Combine(brain, folder)), folder));
        Assert.Contains("README.md, CONVENTIONS.md and 7 area folders", settings.Component.Find("[data-testid='brain-folder-card-message']").TextContent, StringComparison.Ordinal);
        Assert.Empty(settings.Component.FindAll("[data-testid='brain-folder-card-create-conventions']"));
    }

    /// <summary>The requirement's "A README already there" scenario.</summary>
    [Fact]
    public void Create_conventions_leaves_the_persons_readme_unchanged()
    {
        using var settings = RenderSettings();
        var brain = Path.Combine(settings.Root, "Brain");
        Directory.CreateDirectory(brain);
        File.WriteAllText(Path.Combine(brain, "README.md"), "# Mine\n");
        OpenStorageTab(settings.Component);
        settings.Component.Find(Field).Change(brain);

        settings.Component.Find("[data-testid='brain-folder-card-create-conventions']").Click();

        Assert.Equal("# Mine\n", File.ReadAllText(Path.Combine(brain, "README.md")));
        Assert.True(File.Exists(Path.Combine(brain, "CONVENTIONS.md")));
        Assert.Contains("left as it was", settings.Component.Find("[data-testid='brain-folder-card-message']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_folder_inside_a_repository_is_refused_on_the_card()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);
        var repository = Path.Combine(settings.Root, "repo");
        Directory.CreateDirectory(Path.Combine(repository, ".git"));

        settings.Component.Find(Field).Change(Path.Combine(repository, "notes"));

        Assert.Null(settings.Brain.BrainFolder);
        Assert.Contains("outside every repository", settings.Component.Find("[data-testid='brain-folder-card-error']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>Browse is the desktop head's: a host with no picker types the
    /// folder, and one with a picker applies what was picked.</summary>
    [Fact]
    public void Browse_applies_the_folder_the_picker_returns()
    {
        using var settings = RenderSettings(picker: new FixedPicker());
        OpenStorageTab(settings.Component);

        settings.Component.Find("[data-testid='brain-folder-card-browse']").Click();

        settings.Component.WaitForAssertion(() =>
            Assert.Equal(Path.Combine(settings.Root, "Picked"), settings.Brain.BrainFolder));
    }

    [Fact]
    public void Without_a_picker_there_is_no_browse()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        Assert.Empty(settings.Component.FindAll("[data-testid='brain-folder-card-browse']"));
    }

    /// <summary>"Set up your Brain" links here: the link opens Storage, at the
    /// card.</summary>
    [Fact]
    public void The_set_up_link_opens_the_storage_page_at_the_brain_card()
    {
        using var settings = RenderSettings(navigateTo: BrainSetup.SettingsHref);

        Assert.NotEmpty(settings.Component.FindAll("section[aria-label='Storage settings']:not([hidden]) " + Card));
        Assert.Equal("brain-folder", settings.Component.Find(Card).Id);
    }

    [Fact]
    public void A_host_without_the_brain_setting_draws_no_card()
    {
        using var settings = RenderSettings(composeBrain: false);
        OpenStorageTab(settings.Component);

        Assert.Empty(settings.Component.FindAll(Card));
    }

    private static void OpenStorageTab(IRenderedComponent<Settings> component) =>
        SettingsMenu.Open(component, "Storage & backup");

    private static SettingsRenderContext RenderSettings(
        Func<string, SyncedFolderMatch?>? detectSyncedFolder = null,
        IBrainFolderPicker? picker = null,
        string? navigateTo = null,
        bool composeBrain = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-brain-tests", Guid.NewGuid().ToString("n"));

        var storeAppData = Path.Combine(root, "store");
        var store = new WorkspaceSettingsStore(storeAppData, Path.Combine(storeAppData, "settings.json"), _ => null);
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var brain = new BrainSettingsStore(Path.Combine(storeAppData, BrainSettingsStore.FileName));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
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

        if (composeBrain)
        {
            context.Services.AddSingleton<IBrainSettings>(brain);
            context.Services.AddSingleton(new BrainFolderSetup(detectSyncedFolder ?? (_ => null)));
        }

        if (picker is FixedPicker fixedPicker)
        {
            fixedPicker.Folder = Path.Combine(root, "Picked");
        }

        if (picker is not null) context.Services.AddSingleton(picker);

        if (navigateTo is not null)
        {
            context.Services.GetRequiredService<NavigationManager>().NavigateTo(navigateTo);
        }

        // The router hands the page in the address to the screen as its PageId;
        // a test renderer routes nothing, so the address is handed over here.
        var pageId = navigateTo is null ? null : new Uri(new Uri("http://localhost/"), navigateTo).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
        return new SettingsRenderContext(root, brain, context, context.Render<Settings>(parameters => parameters.Add(settings => settings.PageId, pageId)));
    }

    private sealed class FixedPicker : IBrainFolderPicker
    {
        public string? Folder { get; set; }

        public Task<string?> PickFolderAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult(Folder);
    }

    private sealed class NoGitHub : IGitHubClient
    {
        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
        BrainSettingsStore Brain,
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
