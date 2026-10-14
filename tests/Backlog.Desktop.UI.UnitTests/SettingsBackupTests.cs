using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.DomainModels;

using Bunit;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Storage &amp; backup page: a headline that says whether the backlog is
/// backed up, and two cards — where the backlog is and where it is backed up
/// to — with the branch snapshot folder in the first. The working week moved to
/// the page the Dashboard registers, and repositories and GitHub accounts are
/// pages of their own. The devbook-section rows and the disk poll are gone; a
/// devbook belongs to a repository, and the poll's only reason was the shared
/// folder the page now warns against.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SettingsBackupTests
{
    private const string RepositoryInput = "[data-testid='storage-repository-input']";

    [Fact]
    public void The_storage_page_carries_two_cards_and_no_devbook_sections_working_week_or_disk_poll()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        var folderCard = settings.Component.Find("[data-testid='storage-folder-settings']");
        Assert.Equal("Where the backlog is stored", folderCard.QuerySelector(".settings-card__title")?.TextContent);
        Assert.NotNull(folderCard.QuerySelector("[data-testid='storage-path-input']"));
        Assert.NotNull(folderCard.QuerySelector("[data-testid='devbook-cache-settings']"));

        var backupCard = settings.Component.Find("[data-testid='storage-backup-settings']");
        Assert.Equal("Backup", backupCard.QuerySelector(".settings-card__title")?.TextContent);

        Assert.Empty(settings.Component.FindAll("[data-testid='working-hours-settings']"));
        Assert.Empty(settings.Component.FindAll("[data-testid='storage-devbook-folder-settings']"));
        Assert.Empty(settings.Component.FindAll("[data-testid='storage-refresh-settings']"));
    }

    /// <summary>The page is Storage &amp; backup now; what used to be beside the
    /// folder - the repositories and the GitHub accounts - is its own page.</summary>
    [Fact]
    public void The_page_holds_no_repositories_or_github_accounts_content()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        var page = settings.Component.Find("section[aria-label='Storage settings']");

        Assert.Null(page.QuerySelector("[data-testid='github-settings']"));
        Assert.Null(page.QuerySelector("[data-testid='github-accounts-settings']"));
        Assert.DoesNotContain("Signed in with the GitHub CLI", page.TextContent, StringComparison.Ordinal);
    }

    /// <summary>The search finds the page by the cards it draws, without
    /// rendering it.</summary>
    [Fact]
    public void The_search_list_declares_the_cards_the_page_draws()
    {
        var storage = Assert.Single(SettingsPages.BuiltIn, page => page.Id == SettingsPages.Storage);

        Assert.Equal("Storage & backup", storage.Title);
        Assert.NotNull(storage.CardTitles);
        Assert.Contains("Where the backlog is stored", storage.CardTitles);
        Assert.Contains("Backup", storage.CardTitles);
    }

    [Fact]
    public void Before_any_backup_the_headline_says_so_and_the_figures_say_never()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        Assert.Equal("Not backed up yet.", Headline(settings.Component));
        Assert.Contains("Never", settings.Component.Find("[data-testid='storage-backup-last']").TextContent);
        Assert.Contains("0", settings.Component.Find("[data-testid='storage-backup-copies']").TextContent);
    }

    /// <summary>Back up now is the card's primary action, in its header.</summary>
    [Fact]
    public void Back_up_now_is_the_primary_button_in_the_backup_card_header()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        var button = settings.Component.Find(".settings-card__action [data-testid='storage-backup-now']");

        Assert.Contains("btn--primary", button.ClassName);
        Assert.Contains("Back up now", button.TextContent);
    }

    /// <summary>A folder is read character by character, so it is set in mono.</summary>
    [Fact]
    public void The_folder_field_is_set_in_the_mono_font()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        Assert.Contains("setting__input--mono", settings.Component.Find("[data-testid='storage-path-input']").ClassName);
    }

    /// <summary>The field shows empty while snapshots go to the default, and
    /// the status line says where that is — under the storage folder.</summary>
    [Fact]
    public void The_snapshot_folder_defaults_under_the_storage_folder_and_an_override_is_kept()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        Assert.Equal(string.Empty, settings.Component.Find("[data-testid='devbook-cache-directory-input']").GetAttribute("value"));
        var status = settings.Component.Find("[data-testid='devbook-cache-status']").TextContent;
        Assert.Contains(Path.Combine(settings.Store.RootDirectory, "devbook-cache"), status);
        Assert.Contains("under the storage folder", status);

        var elsewhere = Path.Combine(settings.Root, "snapshots");
        Commit(settings.Component.Find("[data-testid='devbook-cache-directory-input']"), elsewhere);

        Assert.Equal(Path.GetFullPath(elsewhere), settings.Store.DevbookCacheDirectory);
        Assert.DoesNotContain("under the storage folder", settings.Component.Find("[data-testid='devbook-cache-status']").TextContent);
    }

    [Fact]
    public void Without_a_repository_the_schedule_waits_and_the_button_is_off()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        Assert.Contains("Name a repository", settings.Component.Find("[data-testid='storage-backup-status']").TextContent);
        Assert.True(settings.Component.Find("[data-testid='storage-backup-now']").HasAttribute("disabled"));
        Assert.Empty(settings.Component.FindAll("[data-testid='storage-backup-time']"));
    }

    [Fact]
    public void Naming_a_repository_and_a_daily_time_arms_the_schedule()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        Commit(settings.Component.Find(RepositoryInput), "JSdotNet/Notes");
        Assert.Equal("JSdotNet/Notes", settings.Store.RootRepository?.FullName);
        Assert.Contains("Never backed up", settings.Component.Find("[data-testid='storage-backup-status']").TextContent);
        Assert.Contains("Nothing is scheduled", settings.Component.Find("[data-testid='storage-backup-status']").TextContent);

        settings.Component.Find("[data-testid='storage-backup-cadence'] select").Change(nameof(BackupCadence.Daily));

        // With seconds, because that is what the browser's change event
        // carried in the harness — the working-week rows accept both, and so
        // does this one.
        settings.Component.Find("[data-testid='storage-backup-time']").Change("07:15:00");

        Assert.Equal(new BackupSchedule(BackupCadence.Daily, new TimeOnly(7, 15), DayOfWeek.Friday), settings.Store.BackupSchedule);
        Assert.Empty(settings.Component.FindAll("[data-testid='storage-backup-day']"));

        var status = settings.Component.Find("[data-testid='storage-backup-status']").TextContent;
        Assert.Contains("Next backup", status);
        Assert.Contains("07:15", status);
        Assert.False(settings.Component.Find("[data-testid='storage-backup-now']").HasAttribute("disabled"));
    }

    [Fact]
    public void Weekly_offers_the_day_and_keeps_it_when_switched_back_to_daily()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);
        Commit(settings.Component.Find(RepositoryInput), "JSdotNet/Notes");

        settings.Component.Find("[data-testid='storage-backup-cadence'] select").Change(nameof(BackupCadence.Weekly));
        settings.Component.Find("[data-testid='storage-backup-day'] select").Change(nameof(DayOfWeek.Sunday));
        Assert.Equal(DayOfWeek.Sunday, settings.Store.BackupSchedule.Day);

        settings.Component.Find("[data-testid='storage-backup-cadence'] select").Change(nameof(BackupCadence.Daily));
        Assert.Empty(settings.Component.FindAll("[data-testid='storage-backup-day']"));
        Assert.Equal(DayOfWeek.Sunday, settings.Store.BackupSchedule.Day);
    }

    /// <summary>The button asks the worker, and what the worker did comes back
    /// through its event onto the line under the button.</summary>
    [Fact]
    public async Task Backing_up_now_reports_what_the_worker_did()
    {
        using var settings = RenderSettings();
        await new SqliteTaskRepository(settings.Store.RootDirectory)
            .SaveAsync(new TaskItem("Back me up", string.Empty, EntryType.Task), TestContext.Current.CancellationToken);
        OpenStorageTab(settings.Component);
        Commit(settings.Component.Find(RepositoryInput), "JSdotNet/Notes");

        settings.Component.Find("[data-testid='storage-backup-now']").Click();

        settings.Component.WaitForAssertion(() =>
        {
            var status = settings.Component.Find("[data-testid='storage-backup-status']");
            Assert.Contains("Last backup", status.TextContent);
            Assert.Contains("a new copy was committed", status.TextContent);
            Assert.Equal("status", status.GetAttribute("role"));
        }, TimeSpan.FromSeconds(10));

        var commit = Assert.Single(settings.GitHub.Commits);
        Assert.Equal(BackupWorker.RepositoryPath, commit.Path);
    }

    /// <summary>A backup that completes while the page is open moves the
    /// headline and both figures, with no navigation in between.</summary>
    [Fact]
    public async Task A_completed_backup_updates_the_headline_and_the_figures()
    {
        using var settings = RenderSettings();
        await new SqliteTaskRepository(settings.Store.RootDirectory)
            .SaveAsync(new TaskItem("Back me up", string.Empty, EntryType.Task), TestContext.Current.CancellationToken);
        OpenStorageTab(settings.Component);
        Commit(settings.Component.Find(RepositoryInput), "JSdotNet/Notes");
        Assert.Equal("Not backed up yet.", Headline(settings.Component));

        settings.Component.Find("[data-testid='storage-backup-now']").Click();

        settings.Component.WaitForAssertion(() =>
        {
            Assert.Equal("Backed up just now — 1 daily copy kept.", Headline(settings.Component));
            Assert.Contains("just now", settings.Component.Find("[data-testid='storage-backup-last']").TextContent);
            var copies = settings.Component.Find("[data-testid='storage-backup-copies']").TextContent;
            Assert.Contains("Keeping", copies);
            Assert.Contains("1", copies);
            Assert.Contains("daily copy", copies);
        }, TimeSpan.FromSeconds(10));
    }

    /// <summary>The headline reads the relative time and the count the worker
    /// kept, in the plural once there is more than one copy.</summary>
    [Fact]
    public void The_headline_reads_the_last_backup_and_the_daily_copies()
    {
        var at = Now.AddHours(-3);
        using var settings = RenderSettings(new BackupState(at, at, true, null) { DailyCopies = 7, LastCopyAt = at });
        OpenStorageTab(settings.Component);

        Assert.Equal("Backed up 3 h ago — 7 daily copies kept.", Headline(settings.Component));
        Assert.Contains("3 h ago", settings.Component.Find("[data-testid='storage-backup-last']").TextContent);
        Assert.Contains("daily copies", settings.Component.Find("[data-testid='storage-backup-copies']").TextContent);
    }

    private static string Headline(IRenderedComponent<Settings> component) =>
        component.Find("[data-testid='settings-headline']").TextContent.Trim();

    [Fact]
    public async Task A_backup_that_failed_is_an_alert_with_githubs_words()
    {
        using var settings = RenderSettings();
        await new SqliteTaskRepository(settings.Store.RootDirectory)
            .SaveAsync(new TaskItem("Back me up", string.Empty, EntryType.Task), TestContext.Current.CancellationToken);
        settings.GitHub.Refusal = new GitHubException("GitHub couldn't find that repository — check the owner/repo and that the token can see it.");
        OpenStorageTab(settings.Component);
        Commit(settings.Component.Find(RepositoryInput), "JSdotNet/Missing");

        settings.Component.Find("[data-testid='storage-backup-now']").Click();

        settings.Component.WaitForAssertion(() =>
        {
            var status = settings.Component.Find("[data-testid='storage-backup-status']");
            Assert.Contains("couldn't find that repository", status.TextContent);
            Assert.Equal("alert", status.GetAttribute("role"));
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Clearing_the_repository_disarms_everything()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);
        Commit(settings.Component.Find(RepositoryInput), "JSdotNet/Notes");
        settings.Component.Find("[data-testid='storage-backup-cadence'] select").Change(nameof(BackupCadence.Daily));

        settings.Component.Find("[data-testid='clear-storage-repository-button']").Click();

        Assert.Null(settings.Store.RootRepository);
        Assert.Contains("Name a repository", settings.Component.Find("[data-testid='storage-backup-status']").TextContent);
        Assert.True(settings.Component.Find("[data-testid='storage-backup-now']").HasAttribute("disabled"));

        // The schedule outlives the repository, so a repository named later
        // starts on it.
        Assert.Equal(BackupCadence.Daily, settings.Store.BackupSchedule.Cadence);
    }

    /// <summary>A field bound the way this screen binds its text fields — the
    /// draft on input, the commit on change — needs both events.</summary>
    private static void Commit(AngleSharp.Dom.IElement field, string value)
    {
        field.Input(value);
        field.Change(value);
    }

    private static void OpenStorageTab(IRenderedComponent<Settings> component) =>
        SettingsMenu.Open(component, "Storage & backup");

    /// <summary>The moment the page and the worker both read, held still:
    /// Tuesday 15 September 2026, 09:00 UTC.</summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 9, 0, 0, TimeSpan.Zero);

    private static SettingsRenderContext RenderSettings(BackupState? state = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-backup-tests", Guid.NewGuid().ToString("n"));

        var storeAppData = Path.Combine(root, "store");
        var store = new WorkspaceSettingsStore(
            storeAppData,
            Path.Combine(storeAppData, "settings.json"),
            _ => null);
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var gitHub = new RecordingGitHub();

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
        context.Services.AddSingleton(new GitHubIntegration(githubSettings, gitHub, new NoProbe()));
        context.Services.AddSingleton<FeedbackReporter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(githubSettings, store));
        context.Services.AddSingleton(new DevbookSourceSelection(githubSettings, new StubBranchCatalog()));

        // The worker the tab resolves, the way both heads compose it: over the
        // workspace store, a client, a state file of this test's own and a clock
        // nothing here advances — the button is what these tests press.
        // The page reads the same clock, so "just now" and "3 h ago" are measured
        // against the moment the worker stamps.
        var time = new FakeTimeProvider(Now);
        var stateStore = new FileBackupStateStore(Path.Combine(root, "backup", "backup-state.json"));
        if (state is not null) stateStore.Save(state);
        var worker = new BackupWorker(store, gitHub, stateStore, time);
        context.Services.AddSingleton(worker);
        context.Services.AddSingleton<TimeProvider>(time);

        return new SettingsRenderContext(root, store, gitHub, worker, context, context.Render<Settings>());
    }

    private sealed class RecordingGitHub : IGitHubClient
    {
        public List<(GitHubRepositoryRef Repository, string Path)> Commits { get; } = [];

        public GitHubException? Refusal { get; set; }

        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default)
        {
            if (Refusal is not null) throw Refusal;

            Commits.Add((repository, path));
            return Task.FromResult(new GitHubCommittedFile(path, "sha", Committed: true));
        }

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
        WorkspaceSettingsStore Store,
        RecordingGitHub GitHub,
        BackupWorker Worker,
        BunitContext TestContext,
        IRenderedComponent<Settings> Component) : IDisposable
    {
        public void Dispose()
        {
            TestContext.Dispose();
            Worker.Dispose();

            SqliteConnection.ClearAllPools();

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
