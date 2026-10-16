using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Archive and Unarchive on a repository's row in Settings (local ADR 0029;
/// requirements "Archiving a repository"): the row stays, marked Archived; the last
/// active repository cannot be archived and says why; archiving offers to move or
/// drop the open tasks, and Keep leaves the offer on the archived row; and GitHub's
/// own word - archived, or not found - only suggests Archive.
/// </summary>
public sealed class SettingsRepositoryArchiveTests
{
    private const string Docs = "JSdotNet/Docs";
    private const string Backlog = "JSdotNet/Backlog";

    // --- Archive and Unarchive on the row ---------------------------------------

    [Fact]
    public void Archiving_a_repository_keeps_its_row_marks_it_archived_and_offers_Unarchive()
    {
        using var settings = RenderSettings();
        SettingsMenu.OpenRepository(settings.Component, "docs");

        settings.Component.Find("[data-testid='archive-repository-button']").Click();

        Assert.True(settings.GitHub.Current.Find("docs")!.Archived);
        Assert.Equal(2, settings.Component.FindAll("[data-testid='repo-edit']").Count);
        var row = settings.Component.Find("tr[data-repository='docs']");
        Assert.Equal("true", row.GetAttribute("data-archived"));
        Assert.Equal("Archived", row.QuerySelector("[data-testid='repo-table-archived']")!.TextContent.Trim());
        Assert.NotNull(settings.Component.Find("[data-testid='unarchive-repository-button']"));
        Assert.Empty(settings.Component.FindAll("[data-testid='archive-repository-button']"));
        Assert.StartsWith("Archived.", settings.Component.Find("[data-testid='repo-archive-note']").TextContent.Trim(), StringComparison.Ordinal);
        Assert.Empty(settings.Tasks.Refiles);
    }

    [Fact]
    public void Archive_sits_between_Rename_and_Remove()
    {
        using var settings = RenderSettings();
        SettingsMenu.OpenRepository(settings.Component, "docs");

        var buttons = settings.Component.FindAll(".repo-card__actions button");

        Assert.Equal(["Rename", "Archive", "Remove repository"], buttons.Select(button => button.TextContent.Trim()));
    }

    [Fact]
    public void The_last_active_repository_cannot_be_archived_and_says_one_must_stay_active()
    {
        using var settings = RenderSettings();
        Assert.Null(settings.GitHub.SetRepositoryArchived("docs", archived: true));
        SettingsMenu.OpenRepository(settings.Component, "backlog");

        var archive = settings.Component.Find("[data-testid='archive-repository-button']");

        Assert.True(archive.HasAttribute("disabled"));
        Assert.Equal(GitHubSettingsStore.LastActiveRepository, settings.Component.Find("[data-testid='repo-archive-status']").TextContent.Trim());
        Assert.False(settings.GitHub.Current.Find("backlog")!.Archived);
    }

    [Fact]
    public void Unarchiving_brings_the_repository_back_and_offers_Archive_again()
    {
        using var settings = RenderSettings();
        Assert.Null(settings.GitHub.SetRepositoryArchived("docs", archived: true));
        SettingsMenu.OpenRepository(settings.Component, "docs");

        settings.Component.Find("[data-testid='unarchive-repository-button']").Click();

        Assert.False(settings.GitHub.Current.Find("docs")!.Archived);
        Assert.Null(settings.Component.Find("tr[data-repository='docs']").GetAttribute("data-archived"));
        Assert.NotNull(settings.Component.Find("[data-testid='archive-repository-button']"));
        Assert.StartsWith("Unarchived.", settings.Component.Find("[data-testid='repo-archive-note']").TextContent.Trim(), StringComparison.Ordinal);
    }

    // --- Moving, dropping or keeping the open tasks -----------------------------

    [Fact]
    public void Archiving_asks_about_the_open_tasks_and_moving_them_files_them_under_the_other_repository()
    {
        using var settings = RenderSettings(tasks: ThreeOpenAndOneDoneUnder(Docs));
        SettingsMenu.OpenRepository(settings.Component, "docs");

        settings.Component.Find("[data-testid='archive-repository-button']").Click();

        var dialog = settings.Component.Find("[data-testid='repo-archive-dialog']");
        Assert.Contains("3 open tasks are filed under JSdotNet/Docs", dialog.TextContent, StringComparison.Ordinal);
        Assert.False(settings.GitHub.Current.Find("docs")!.Archived);
        Assert.True(settings.Component.Find("[data-testid='repo-archive-move']").HasAttribute("disabled"));

        settings.Component.Find("[data-testid='repo-archive-move-to'] select").Change(Backlog);
        settings.Component.Find("[data-testid='repo-archive-move']").Click();

        Assert.Equal([(Docs, (string?)Backlog, false)], settings.Tasks.Refiles);
        Assert.True(settings.GitHub.Current.Find("docs")!.Archived);
        Assert.Empty(settings.Component.FindAll("[data-testid='repo-archive-dialog']"));
        Assert.Equal("Archived. 3 open tasks moved to JSdotNet/Backlog.", settings.Component.Find("[data-testid='repo-archive-note']").TextContent.Trim());
    }

    [Fact]
    public void The_open_tasks_can_only_move_to_another_active_repository()
    {
        using var settings = RenderSettings(tasks: ThreeOpenAndOneDoneUnder(Docs), repositories: "backlog = JSdotNet/Backlog\ndocs = JSdotNet/Docs\nold = JSdotNet/Old");
        Assert.Null(settings.GitHub.SetRepositoryArchived("old", archived: true));
        SettingsMenu.OpenRepository(settings.Component, "docs");

        settings.Component.Find("[data-testid='archive-repository-button']").Click();

        var options = settings.Component.FindAll("[data-testid='repo-archive-move-to'] option")
            .Select(option => option.GetAttribute("value"))
            .Where(value => !string.IsNullOrEmpty(value));
        Assert.Equal([Backlog], options);
    }

    [Fact]
    public void Archiving_and_dropping_the_open_tasks_drops_them_and_archives()
    {
        using var settings = RenderSettings(tasks: ThreeOpenAndOneDoneUnder(Docs));
        SettingsMenu.OpenRepository(settings.Component, "docs");

        settings.Component.Find("[data-testid='archive-repository-button']").Click();
        settings.Component.Find("[data-testid='repo-archive-drop']").Click();

        Assert.Equal([(Docs, (string?)null, true)], settings.Tasks.Refiles);
        Assert.True(settings.GitHub.Current.Find("docs")!.Archived);
        Assert.Equal(
            "Archived. 3 open tasks dropped; each keeps its filing under JSdotNet/Docs.",
            settings.Component.Find("[data-testid='repo-archive-note']").TextContent.Trim());
    }

    [Fact]
    public void Keeping_the_open_tasks_archives_and_the_archived_row_offers_the_choice_later()
    {
        using var settings = RenderSettings(tasks: ThreeOpenAndOneDoneUnder(Docs));
        SettingsMenu.OpenRepository(settings.Component, "docs");

        settings.Component.Find("[data-testid='archive-repository-button']").Click();
        settings.Component.Find("[data-testid='repo-archive-keep']").Click();

        Assert.Empty(settings.Tasks.Refiles);
        Assert.True(settings.GitHub.Current.Find("docs")!.Archived);
        Assert.Empty(settings.Component.FindAll("[data-testid='repo-archive-dialog']"));

        settings.Component.Find("[data-testid='repo-open-tasks-button']").Click();

        var dialog = settings.Component.Find("[data-testid='repo-archive-dialog']");
        Assert.Contains("Open tasks of JSdotNet/Docs", dialog.TextContent, StringComparison.Ordinal);

        settings.Component.Find("[data-testid='repo-archive-drop']").Click();

        Assert.Equal([(Docs, (string?)null, true)], settings.Tasks.Refiles);
        Assert.True(settings.GitHub.Current.Find("docs")!.Archived);
    }

    /// <summary>The tasks go first: a refusal from the Tasks module leaves the
    /// dialog open with its sentence and the repository as it was.</summary>
    [Fact]
    public void A_refused_move_leaves_the_repository_active_and_says_why()
    {
        using var settings = RenderSettings(tasks: ThreeOpenAndOneDoneUnder(Docs), refileRefuses: true);
        SettingsMenu.OpenRepository(settings.Component, "docs");

        settings.Component.Find("[data-testid='archive-repository-button']").Click();
        settings.Component.Find("[data-testid='repo-archive-drop']").Click();

        Assert.False(settings.GitHub.Current.Find("docs")!.Archived);
        Assert.Equal("The tasks could not be refiled.", settings.Component.Find("[data-testid='repo-archive-error']").TextContent.Trim());
    }

    // --- GitHub's own word suggests, never archives -----------------------------

    [Fact]
    public void A_repository_GitHub_reports_archived_suggests_Archive_and_stays_active()
    {
        using var settings = RenderSettings(gitHubStates: new() { [Docs] = GitHubRepositoryState.Archived });
        SettingsMenu.Open(settings.Component, "Repositories");

        var row = settings.Component.Find("tr[data-repository='docs']");
        Assert.Equal("Archived on GitHub", row.QuerySelector("[data-testid='repo-table-github-hint']")!.TextContent.Trim());
        Assert.False(settings.GitHub.Current.Find("docs")!.Archived);

        SettingsMenu.EditRepository(settings.Component, "docs");

        Assert.StartsWith("GitHub reports this repository archived.", settings.Component.Find("[data-testid='repo-github-hint']").TextContent.Trim(), StringComparison.Ordinal);

        settings.Component.Find("[data-testid='repo-github-hint-archive']").Click();

        Assert.True(settings.GitHub.Current.Find("docs")!.Archived);
        Assert.Empty(settings.Component.FindAll("[data-testid='repo-table-github-hint']"));
    }

    [Fact]
    public void A_repository_GitHub_cannot_find_says_so_and_suggests_Archive()
    {
        using var settings = RenderSettings(gitHubStates: new() { [Docs] = GitHubRepositoryState.NotFound });
        SettingsMenu.OpenRepository(settings.Component, "docs");

        Assert.Equal("Not found on GitHub", settings.Component.Find("tr[data-repository='docs'] [data-testid='repo-table-github-hint']").TextContent.Trim());
        Assert.StartsWith("GitHub cannot find this repository.", settings.Component.Find("[data-testid='repo-github-hint']").TextContent.Trim(), StringComparison.Ordinal);
        Assert.NotNull(settings.Component.Find("[data-testid='repo-github-hint-archive']"));
        Assert.False(settings.GitHub.Current.Find("docs")!.Archived);
    }

    /// <summary>On the last active repository GitHub's word is still said, on the
    /// row and on the card, but neither invites an Archive the card cannot do.</summary>
    [Fact]
    public void GitHubs_word_on_the_last_active_repository_is_said_without_offering_Archive()
    {
        using var settings = RenderSettings(gitHubStates: new() { [Backlog] = GitHubRepositoryState.Archived }, archiveDocsFirst: true);
        SettingsMenu.Open(settings.Component, "Repositories");

        var badge = settings.Component.Find("tr[data-repository='backlog'] [data-testid='repo-table-github-hint']");
        Assert.Equal("GitHub reports this repository archived.", badge.GetAttribute("title"));

        SettingsMenu.EditRepository(settings.Component, "backlog");

        Assert.Equal(GitHubSettingsStore.LastActiveRepository, settings.Component.Find("[data-testid='repo-archive-status']").TextContent.Trim());
        Assert.Equal("GitHub reports this repository archived.", settings.Component.Find("[data-testid='repo-github-hint']").TextContent.Trim());
        Assert.Empty(settings.Component.FindAll("[data-testid='repo-github-hint-archive']"));
    }

    [Fact]
    public void A_repository_archived_here_is_not_asked_about_on_GitHub()
    {
        using var settings = RenderSettings(gitHubStates: new() { [Backlog] = GitHubRepositoryState.Active }, archiveDocsFirst: true);
        SettingsMenu.Open(settings.Component, "Repositories");

        Assert.Equal([Backlog], settings.Client.Asked);
        Assert.Empty(settings.Component.FindAll("[data-testid='repo-table-github-hint']"));
    }

    private static IReadOnlyList<TaskItemDto> ThreeOpenAndOneDoneUnder(string repositoryId) =>
    [
        Entry("Draft one", EntryStatus.Draft, repositoryId),
        Entry("Ready one", EntryStatus.Ready, repositoryId),
        Entry("Under way", EntryStatus.InProgress, repositoryId),
        Entry("Finished", EntryStatus.Done, repositoryId),
        Entry("Elsewhere", EntryStatus.Ready, Backlog),
    ];

    private static TaskItemDto Entry(string title, EntryStatus status, string repositoryId) =>
        new(Guid.NewGuid(), title, string.Empty, EntryType.Task, Priority.Medium, status, null, [], 0, 0, 0, [], RepoIds: [repositoryId]);

    private static SettingsRenderContext RenderSettings(
        IReadOnlyList<TaskItemDto>? tasks = null,
        bool refileRefuses = false,
        Dictionary<string, GitHubRepositoryState>? gitHubStates = null,
        bool archiveDocsFirst = false,
        string repositories = "backlog = JSdotNet/Backlog\ndocs = JSdotNet/Docs")
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-archive-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        // The hint is read only while the GitHub integration is on.
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, gitHubStates is not null);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"), () => Path.Combine(root, "workspace"));
        var (parsed, errors) = GitHubSettings.ParseText(repositories);
        Assert.Empty(errors);
        Assert.Null(githubSettings.SetRepositories(parsed));
        if (archiveDocsFirst) Assert.Null(githubSettings.SetRepositoryArchived("docs", archived: true));

        var taskItems = new RecordingTaskItems(tasks ?? [], refileRefuses);
        var client = new StateClient(gitHubStates ?? []);

        var context = new BunitContext();
        // The move-or-drop dialog's Modal moves the focus when it opens, which is a
        // JS call and nothing these tests are about.
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
        context.Services.AddSingleton(new GitHubIntegration(githubSettings, client, new NoProbe()));
        context.Services.AddSingleton<FeedbackReporter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(githubSettings, store));
        context.Services.AddSingleton(new DevbookSourceSelection(githubSettings, new StubBranchCatalog()));
        context.Services.AddSingleton<ITaskItems>(taskItems);

        return new SettingsRenderContext(root, context, context.Render<Settings>(), githubSettings, taskItems, client);
    }

    /// <summary>Lists the tasks the test chose and records each refile it is asked
    /// for; every other member is out of this page's reach.</summary>
    private sealed class RecordingTaskItems(IReadOnlyList<TaskItemDto> tasks, bool refuse) : ITaskItems
    {
        public List<(string RepositoryId, string? MoveToId, bool Drop)> Refiles { get; } = [];

        public Task<Result<int>> RefileRepositoryTasksAsync(string repositoryId, string? moveToId, bool drop, CancellationToken cancellationToken = default)
        {
            if (refuse) return Task.FromResult(Result.Failure<int>(Error.Unexpected("tasks.refile", "The tasks could not be refiled.")));

            Refiles.Add((repositoryId, moveToId, drop));
            var open = tasks.Count(task =>
                task.Status is EntryStatus.Draft or EntryStatus.Ready or EntryStatus.InProgress
                && (task.RepoIds ?? []).Contains(repositoryId, StringComparer.OrdinalIgnoreCase));
            return Task.FromResult(Result.Success(open));
        }

        public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(tasks);

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<SavedTaskDto>> SaveFromTextAsync(Guid? id, string rawText, int order, string? sourceInboxId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<TaskItemDto>> LinkToIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<TaskItemDto>> UnlinkFromIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<TaskItemDto>> SetDevbookReferencesAsync(Guid id, IReadOnlyList<string> references, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(0));

        public Task<Result<ImportPlanResultDto>> ImportPlanAsync(string rawText, string? defaultRepo = null, IReadOnlyDictionary<string, string>? repoMatches = null, string? sourceInboxId = null, IReadOnlyDictionary<string, string>? sourceInboxIds = null, bool layOutOnRoadmap = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Answers GitHub's word on a repository from the test's table, and
    /// records which repositories were asked about at all.</summary>
    private sealed class StateClient(Dictionary<string, GitHubRepositoryState> states) : IGitHubClient
    {
        public List<string> Asked { get; } = [];

        public Task<GitHubRepositoryState> GetRepositoryStateAsync(GitHubRepositoryRef repository, CancellationToken cancellationToken = default)
        {
            lock (Asked) Asked.Add(repository.FullName);
            return states.TryGetValue(repository.FullName, out var state)
                ? Task.FromResult(state)
                : Task.FromException<GitHubRepositoryState>(new GitHubException("Not asked about in this test."));
        }

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
        GitHubSettingsStore GitHub,
        RecordingTaskItems Tasks,
        StateClient Client) : IDisposable
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
