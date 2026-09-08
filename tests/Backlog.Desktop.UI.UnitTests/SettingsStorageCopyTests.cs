using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the Storage tab tells the reader to do with the folder.
/// <para>
/// This screen is the only place the app gives storage advice, and the advice it
/// used to give - every entry is its own markdown file, so point this at a synced
/// folder - stopped being true when local ADR 0003 made the store one SQLite
/// database. Somebody followed it onto OneDrive, which cannot merge a binary
/// file, and lost committed status edits to six conflicted copies. That is R9 in
/// <c>.arc42/11-risks-and-technical-debt.md</c>, and its mitigation says the copy
/// must be corrected regardless of when sync ships.
/// </para>
/// <para>
/// Pinned in a test rather than left to review because the sentence was wrong for
/// three releases without anybody noticing, and because the absence of the old
/// advice is the half that actually protects the reader: new copy can be added
/// alongside a retired warning and read as complete.
/// </para>
/// </summary>
public sealed class SettingsStorageCopyTests
{
    /// <summary>The fact the reader needs first, because it is what makes the
    /// warning below follow: one file, not a folder of them.</summary>
    [Fact]
    public void The_copy_says_the_backlog_is_a_single_database_file()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        var copy = Description(settings.Component);

        Assert.Contains("one SQLite database", copy, StringComparison.Ordinal);
        Assert.Contains("single binary file", copy, StringComparison.Ordinal);
    }

    /// <summary>
    /// The warning names a product rather than only describing a category. "Do not
    /// use a file-sync folder" is not advice somebody recognises their own OneDrive
    /// folder in, and recognising it is the entire point.
    /// </summary>
    [Fact]
    public void The_copy_warns_that_a_file_sync_folder_loses_edits()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        var copy = Description(settings.Component);

        Assert.Contains("OneDrive", copy, StringComparison.Ordinal);
        Assert.Contains("local", copy, StringComparison.Ordinal);
        Assert.Contains("lose edits", copy, StringComparison.Ordinal);
    }

    /// <summary>
    /// The regression that matters. Asserted over the whole tab and not just the
    /// one paragraph, so the retired advice cannot come back somewhere else on the
    /// screen and still pass.
    /// </summary>
    [Fact]
    public void The_tab_no_longer_advises_pointing_the_folder_at_a_synced_one()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        // Tabs names its panels "tabpanel-{id}", and a panel renders its content
        // only while it is the active one - hence opening the tab above.
        var tab = settings.Component.Find("#tabpanel-storage").TextContent;

        Assert.DoesNotContain("markdown file with YAML", tab, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("travels with it", tab, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("synced or version-controlled", tab, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The abstract warning above tells the reader what a file-sync folder does
    /// to the database. It cannot tell them they are in one, and R9 was somebody
    /// who was: the copy is now correct and their root is still on OneDrive. So
    /// the concrete warning sits beside the general one and names the provider,
    /// because a name is what somebody recognises their own folder in.
    /// </summary>
    [Fact]
    public void The_tab_names_the_sync_provider_the_root_turned_out_to_be_inside()
    {
        using var settings = RenderSettings(_ => new SyncedFolderMatch("OneDrive", @"C:\Users\dev\OneDrive"));
        OpenStorageTab(settings.Component);

        var warning = settings.Component.Find(SyncWarning);

        Assert.Contains("OneDrive", warning.TextContent, StringComparison.Ordinal);

        // Not colour alone: it is announced, and the folder field points at it
        // the way it already points at its own status line.
        Assert.Equal("alert", warning.GetAttribute("role"));
        Assert.Contains(
            "storage-path-sync-warning",
            settings.Component.Find(StoragePathInput).GetAttribute("aria-describedby") ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Detection is heuristic, so the folder nobody syncs must read exactly as it
    /// read before this warning existed. A false positive here would be worse
    /// than the gap it closes.
    /// </summary>
    [Fact]
    public void The_tab_says_nothing_extra_about_an_ordinary_folder()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        Assert.Empty(settings.Component.FindAll(SyncWarning));
        Assert.DoesNotContain(
            "storage-path-sync-warning",
            settings.Component.Find(StoragePathInput).GetAttribute("aria-describedby") ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>Changing the folder is the other half of "at startup, and when
    /// the root is changed": the warning follows the root the screen just
    /// applied, with no restart in between.</summary>
    [Fact]
    public void Applying_a_synced_folder_warns_without_a_restart()
    {
        using var settings = RenderSettings(ProviderFolderNamed("OneDrive"));
        OpenStorageTab(settings.Component);

        Assert.Empty(settings.Component.FindAll(SyncWarning));

        var synced = Path.Combine(settings.Root, "OneDrive", "backlog");
        Commit(settings.Component, synced);

        Assert.Contains("OneDrive", settings.Component.Find(SyncWarning).TextContent, StringComparison.Ordinal);

        // Warn, do not prevent. Over a synced root the field still takes a path
        // and the way back to the default folder is still live - the warning is
        // the whole of what the app does about it.
        Assert.False(settings.Component.Find(StoragePathInput).HasAttribute("disabled"));
        Assert.False(settings.Component.Find("[data-testid='reset-storage-path']").HasAttribute("disabled"));

        // And moving back out of it leaves the tab as it was, rather than
        // warning about a folder the app no longer uses.
        Commit(settings.Component, Path.Combine(settings.Root, "local", "backlog"));

        Assert.Empty(settings.Component.FindAll(SyncWarning));
    }

    private const string SyncWarning = "[data-testid='storage-path-sync-warning']";

    private const string StoragePathInput = "[data-testid='storage-path-input']";

    /// <summary>Types a folder in and commits it, the way the field is wired:
    /// the value follows every keystroke and the change is what applies it.</summary>
    private static void Commit(IRenderedComponent<Settings> component, string path)
    {
        var field = component.Find(StoragePathInput);
        field.Input(path);
        field.Change(path);
    }

    /// <summary>A fake provider that claims any folder named after it, so a test
    /// can point the screen at one inside its own temporary root instead of at a
    /// real OneDrive.</summary>
    private static Func<string, SyncedFolderMatch?> ProviderFolderNamed(string providerName) =>
        root => root.Contains(Path.DirectorySeparatorChar + providerName, StringComparison.OrdinalIgnoreCase)
            ? new SyncedFolderMatch(providerName, providerName)
            : null;

    private static string Description(IRenderedComponent<Settings> component) =>
        component.Find("[data-testid='storage-path-description']").TextContent;

    private static void OpenStorageTab(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "Storage").Click();

    /// <summary>Renders the screen over a throwaway workspace, with a sync probe
    /// that finds nothing unless a test hands it one.
    /// <para>
    /// Never the real detector, not even for the folder that is meant to raise no
    /// warning: that would put the assertion on this machine's %TEMP% rather than
    /// on the screen. A profile relocated onto OneDrive is not a hypothetical
    /// machine either — it is the population R9 is about — and on one of those
    /// every negative here would go red for the one reason that is not a defect.
    /// What the heuristic itself recognises is asserted against a fake machine in
    /// <c>SyncedFolderDetectorTests</c>.
    /// </para></summary>
    private static SettingsRenderContext RenderSettings(Func<string, SyncedFolderMatch?>? detectSyncedFolder = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-storage-copy-tests", Guid.NewGuid().ToString("n"));

        var storeAppData = Path.Combine(root, "store");
        var store = new WorkspaceSettingsStore(
            storeAppData,
            Path.Combine(storeAppData, "settings.json"),
            detectSyncedFolder ?? (_ => null));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<ITasksRefreshSettings>(
            new TasksRefreshSettingsStore(Path.Combine(root, "refresh", "refresh.json")));
        context.Services.AddSingleton<IWorkingHoursSettings>(
            new WorkingHoursSettingsStore(Path.Combine(root, "working-hours", "working-hours.json")));
        context.Services.AddSingleton(new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json")));
        context.Services.AddSingleton(new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json")));
        context.Services.AddSingleton(new GitHubIntegration(githubSettings, new NoGitHub(), new NoProbe()));
        context.Services.AddSingleton<FeedbackReporter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(githubSettings, store));
        context.Services.AddSingleton(new KnowledgeSourceSelection(githubSettings, new StubBranchCatalog()));

        return new SettingsRenderContext(root, context, context.Render<Settings>());
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
