using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.DomainModels;

using Bunit;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What committing a folder on the Storage tab does with the backlog that is
/// already there.
/// <para>
/// It used to do nothing with it: the field and "Use the default folder" both
/// only repointed the app, and the note underneath said so in the small print.
/// Somebody whose root was on OneDrive pressed the button to get off it and
/// watched every task disappear, because the database had stayed behind. So
/// committing a folder now takes the backlog along, and leaving it behind is
/// the explicit second action rather than the silent default.
/// </para>
/// </summary>
public sealed class SettingsStorageMoveTests
{
    [Fact]
    public async Task Committing_a_folder_takes_the_backlog_along()
    {
        using var settings = RenderSettings();
        var task = new TaskItem("Comes along", string.Empty, EntryType.Task);
        await new SqliteTaskRepository(settings.Store.RootDirectory).SaveAsync(task, TestContext.Current.CancellationToken);
        OpenStorageTab(settings.Component);
        var target = Path.Combine(settings.Root, "elsewhere");

        Commit(settings.Component, target);

        Assert.Equal(target, settings.Store.RootDirectory);
        Assert.NotNull(await new SqliteTaskRepository(target).GetAsync(task.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>The status says which of the two things happened, and that
    /// the old folder still has its copy - the sentence that tells somebody
    /// they can go and delete it themselves once they are sure.</summary>
    [Fact]
    public async Task The_status_says_the_backlog_was_copied_and_where_the_old_copy_is()
    {
        using var settings = RenderSettings();
        var previous = settings.Store.RootDirectory;
        await new SqliteTaskRepository(previous).SaveAsync(new TaskItem("Any", string.Empty, EntryType.Task), TestContext.Current.CancellationToken);
        OpenStorageTab(settings.Component);

        Commit(settings.Component, Path.Combine(settings.Root, "elsewhere"));

        var status = Status(settings.Component);
        Assert.Contains("Moved", status, StringComparison.Ordinal);
        Assert.Contains("copied", status, StringComparison.Ordinal);
        Assert.Contains(previous, status, StringComparison.Ordinal);
    }

    /// <summary>
    /// Enter pressed again on a field that now names the folder the app is
    /// already in must not wipe the sentence the first press put on screen -
    /// that sentence is where the old copy is.
    /// </summary>
    [Fact]
    public async Task Committing_the_same_folder_again_keeps_the_moved_status()
    {
        using var settings = RenderSettings();
        var previous = settings.Store.RootDirectory;
        await new SqliteTaskRepository(previous).SaveAsync(new TaskItem("Any", string.Empty, EntryType.Task), TestContext.Current.CancellationToken);
        OpenStorageTab(settings.Component);
        var target = Path.Combine(settings.Root, "elsewhere");

        var field = settings.Component.Find(StoragePathInput);
        field.Input(target);
        field.KeyDown("Enter");
        settings.Component.Find(StoragePathInput).KeyDown("Enter");

        Assert.Equal(target, settings.Store.RootDirectory);
        var status = Status(settings.Component);
        Assert.Contains("Moved", status, StringComparison.Ordinal);
        Assert.Contains(previous, status, StringComparison.Ordinal);
    }

    /// <summary>
    /// The field commits nothing on its own. It used to move on its change
    /// event, which fires on blur - so typing a folder and clicking "Switch
    /// without moving" moved the backlog first and then switched to it, and
    /// the button could never do what it said. Leaving the field now leaves
    /// everything where it was; the buttons and Enter are the commits.
    /// </summary>
    [Fact]
    public async Task Leaving_the_field_moves_nothing()
    {
        using var settings = RenderSettings();
        var previous = settings.Store.RootDirectory;
        await new SqliteTaskRepository(previous).SaveAsync(new TaskItem("Stays put", string.Empty, EntryType.Task), TestContext.Current.CancellationToken);
        OpenStorageTab(settings.Component);
        var target = Path.Combine(settings.Root, "elsewhere");

        var field = settings.Component.Find(StoragePathInput);
        field.Input(target);

        // No handler at all, which is the whole of the guarantee: nothing the
        // field does on its own can reach the store.
        Assert.Throws<MissingEventHandlerException>(() => field.Change(target));
        Assert.Equal(previous, settings.Store.RootDirectory);
        Assert.False(File.Exists(Path.Combine(target, "backlog.db")));
    }

    /// <summary>The move has a button of its own, live once the field names
    /// a folder other than the one the app is in. "Move to the default
    /// folder" used to be the only move with a button, and the folder
    /// somebody typed had nothing that said "move" over it.</summary>
    [Fact]
    public void The_move_button_wakes_up_when_a_different_folder_is_typed()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        Assert.True(settings.Component.Find(MoveButton).HasAttribute("disabled"));

        settings.Component.Find(StoragePathInput).Input(Path.Combine(settings.Root, "elsewhere"));

        Assert.False(settings.Component.Find(MoveButton).HasAttribute("disabled"));
    }

    /// <summary>The button that lost the backlog. It is the same move now, with
    /// the default folder typed in for you.</summary>
    [Fact]
    public async Task Returning_to_the_default_folder_takes_the_backlog_along_too()
    {
        using var settings = RenderSettings();
        var elsewhere = Path.Combine(settings.Root, "elsewhere");
        Assert.Null(settings.Store.TryUseRoot(elsewhere));
        var task = new TaskItem("Back home", string.Empty, EntryType.Task);
        await new SqliteTaskRepository(elsewhere).SaveAsync(task, TestContext.Current.CancellationToken);
        OpenStorageTab(settings.Component);

        settings.Component.Find("[data-testid='reset-storage-path']").Click();

        Assert.True(settings.Store.IsDefaultRoot);
        Assert.NotNull(await new SqliteTaskRepository(settings.Store.RootDirectory).GetAsync(task.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>A folder that already holds a backlog is somebody's backlog.
    /// The screen refuses in its own status line and stays where it was.</summary>
    [Fact]
    public async Task A_folder_that_already_holds_a_backlog_is_refused_not_overwritten()
    {
        using var settings = RenderSettings();
        var previous = settings.Store.RootDirectory;
        await new SqliteTaskRepository(previous).SaveAsync(new TaskItem("Mine", string.Empty, EntryType.Task), TestContext.Current.CancellationToken);
        var occupied = Path.Combine(settings.Root, "occupied");
        var theirs = new TaskItem("Theirs", string.Empty, EntryType.Task);
        await new SqliteTaskRepository(occupied).SaveAsync(theirs, TestContext.Current.CancellationToken);
        OpenStorageTab(settings.Component);

        Commit(settings.Component, occupied);

        Assert.Equal(previous, settings.Store.RootDirectory);
        Assert.Contains("already holds a backlog", Status(settings.Component), StringComparison.Ordinal);
        var only = Assert.Single(await new SqliteTaskRepository(occupied).ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Theirs", only.Title);
    }

    /// <summary>The old behaviour, still reachable: open the backlog that lives
    /// in the folder typed in, copying nothing there.</summary>
    [Fact]
    public async Task Switching_without_moving_leaves_the_backlog_behind()
    {
        using var settings = RenderSettings();
        var previous = settings.Store.RootDirectory;
        await new SqliteTaskRepository(previous).SaveAsync(new TaskItem("Stays", string.Empty, EntryType.Task), TestContext.Current.CancellationToken);
        OpenStorageTab(settings.Component);
        var target = Path.Combine(settings.Root, "elsewhere");

        settings.Component.Find(StoragePathInput).Input(target);
        settings.Component.Find("[data-testid='switch-storage-path']").Click();

        Assert.Equal(target, settings.Store.RootDirectory);
        Assert.False(File.Exists(Path.Combine(target, "backlog.db")));
        var status = Status(settings.Component);
        Assert.Contains("Switched", status, StringComparison.Ordinal);
        Assert.Contains("nothing was copied", status, StringComparison.Ordinal);
    }

    /// <summary>The note is the small print somebody reads before pressing
    /// anything. It has to describe the move, not the repoint it used to.</summary>
    [Fact]
    public void The_note_says_the_backlog_moves_with_the_folder()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        // Collapsed, because the note wraps in the markup wherever it wraps
        // and a phrase must not fail for landing on a line break.
        var tab = System.Text.RegularExpressions.Regex.Replace(
            settings.Component.Find("#tabpanel-storage").TextContent, @"\s+", " ");

        Assert.DoesNotContain("does not copy anything", tab, StringComparison.Ordinal);
        Assert.Contains("copied there", tab, StringComparison.Ordinal);
        Assert.Contains("never written over", tab, StringComparison.Ordinal);
        // And it no longer promises "everything beside it": only the app's own
        // files come, and the note says what stays.
        Assert.DoesNotContain("everything beside it", tab, StringComparison.Ordinal);
        Assert.Contains("stays where it is", tab, StringComparison.Ordinal);
    }

    private const string StoragePathInput = "[data-testid='storage-path-input']";

    private const string MoveButton = "[data-testid='move-storage-path']";

    private static string Status(IRenderedComponent<Settings> component) =>
        component.Find("[data-testid='storage-path-status']").TextContent;

    /// <summary>Types a folder in and moves to it, the way the screen is
    /// wired: the value follows every keystroke and the button is what
    /// applies it.</summary>
    private static void Commit(IRenderedComponent<Settings> component, string path)
    {
        component.Find(StoragePathInput).Input(path);
        component.Find(MoveButton).Click();
    }

    private static void OpenStorageTab(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "Storage").Click();

    /// <summary>Renders the screen over a throwaway workspace with a sync probe
    /// that finds nothing, for the reason SettingsStorageCopyTests gives.</summary>
    private static SettingsRenderContext RenderSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-storage-move-tests", Guid.NewGuid().ToString("n"));

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

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IWorkingHoursSettings>(
            new WorkingHoursSettingsStore(Path.Combine(root, "working-hours", "working-hours.json")));
        context.Services.AddSingleton<ICaptureSourceSettings>(
            new CaptureSourcesSettingsStore(Path.Combine(root, "capture", "capture-sources.json")));
        context.Services.AddSingleton(new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json")));
        context.Services.AddSingleton(new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json")));
        context.Services.AddSingleton(new GitHubIntegration(githubSettings, new NoGitHub(), new NoProbe()));
        context.Services.AddSingleton<FeedbackReporter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(githubSettings, store));
        context.Services.AddSingleton(new DevbookSourceSelection(githubSettings, new StubBranchCatalog()));

        return new SettingsRenderContext(root, store, context, context.Render<Settings>());
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
        WorkspaceSettingsStore Store,
        BunitContext TestContext,
        IRenderedComponent<Settings> Component) : IDisposable
    {
        public void Dispose()
        {
            TestContext.Dispose();

            // The repositories these tests open keep pooled handles on their
            // databases, and a pooled handle is a folder Windows will not delete.
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
