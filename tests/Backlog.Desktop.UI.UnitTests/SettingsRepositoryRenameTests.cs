using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A repository renamed on GitHub, applied by editing its line in Settings. The
/// store carries its own row; what this page owes on top is the call into each
/// module that holds entries filed against the old id, and a sentence saying
/// what it did. Asserted against the recorded calls and the status line, because
/// a page that saved the new id and told nobody would leave every entry pointing
/// at a coordinate the next start re-registers as a ghost.
/// </summary>
public sealed class SettingsRepositoryRenameTests
{
    [Fact]
    public void Changing_only_the_owner_name_re_points_entries_and_inbox_items_and_says_so()
    {
        using var settings = RenderSettings(entriesMoved: 12, itemsMoved: 1);
        OpenRepositoriesTab(settings.Component);

        Retype(settings.Component, "backlog = JSdotNet/Backlog-renamed\ndocs = JSdotNet/Docs");

        Assert.Equal([("JSdotNet/Backlog", "JSdotNet/Backlog-renamed")], settings.Tasks.Renames);
        Assert.Equal([("JSdotNet/Backlog", "JSdotNet/Backlog-renamed")], settings.Inbox.Renames);
        Assert.Equal(
            "Renamed JSdotNet/Backlog to JSdotNet/Backlog-renamed; 12 entries and 1 inbox item followed it.",
            settings.Component.Find("[data-testid='github-repos-rename-note']").TextContent.Trim());
        Assert.Empty(settings.Component.FindAll(".setting__status--error"));
    }

    [Fact]
    public void Relabelling_an_alias_calls_neither_module_and_shows_no_note()
    {
        using var settings = RenderSettings();
        OpenRepositoriesTab(settings.Component);

        Retype(settings.Component, "bl = JSdotNet/Backlog\ndocs = JSdotNet/Docs");

        Assert.Empty(settings.Tasks.Renames);
        Assert.Empty(settings.Inbox.Renames);
        Assert.Empty(settings.Component.FindAll("[data-testid='github-repos-rename-note']"));
    }

    /// <summary>The note is about the save that did the renaming. The next save
    /// that renames nothing clears it, so a stale sentence never sits under the
    /// box describing an earlier edit.</summary>
    [Fact]
    public void The_note_clears_on_the_next_save_that_renames_nothing()
    {
        using var settings = RenderSettings(entriesMoved: 1);
        OpenRepositoriesTab(settings.Component);

        Retype(settings.Component, "backlog = JSdotNet/Backlog-renamed\ndocs = JSdotNet/Docs");
        Assert.NotEmpty(settings.Component.FindAll("[data-testid='github-repos-rename-note']"));

        Retype(settings.Component, "backlog = JSdotNet/Backlog-renamed\ndocs = JSdotNet/Docs\nother = Someone/Other");

        Assert.Empty(settings.Component.FindAll("[data-testid='github-repos-rename-note']"));
        Assert.Single(settings.Tasks.Renames);
    }

    /// <summary>A module that refuses is reported, not swallowed: the registry has
    /// moved on and an entry left behind is the one thing to know about.</summary>
    [Fact]
    public void A_module_that_refuses_is_reported_in_the_status_line()
    {
        using var settings = RenderSettings(tasksRefuse: true);
        OpenRepositoriesTab(settings.Component);

        Retype(settings.Component, "backlog = JSdotNet/Backlog-renamed\ndocs = JSdotNet/Docs");

        Assert.Contains("could not follow", settings.Component.Find(".setting__status--error").TextContent, StringComparison.Ordinal);
        Assert.Equal("JSdotNet/Backlog-renamed", settings.GitHub.Current.Find("backlog")!.FullName);
    }

    private static void Retype(IRenderedComponent<Settings> component, string text)
    {
        component.Find("[data-testid='github-repos-input']").Input(text);
        component.Find("[data-testid='github-repos-input']").Change(text);
    }

    private static void OpenRepositoriesTab(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "Repositories").Click();

    private static SettingsRenderContext RenderSettings(int entriesMoved = 0, int itemsMoved = 0, bool tasksRefuse = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-rename-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"), () => Path.Combine(root, "workspace"));
        var (repositories, errors) = GitHubSettings.ParseText("backlog = JSdotNet/Backlog\ndocs = JSdotNet/Docs");
        Assert.Empty(errors);
        Assert.Null(githubSettings.SetRepositories(repositories));

        var tasks = new RecordingTaskItems(entriesMoved, tasksRefuse);
        var inbox = new RecordingInboxItems(itemsMoved);

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
        context.Services.AddSingleton<ITaskItems>(tasks);
        context.Services.AddSingleton<IInboxItems>(inbox);

        return new SettingsRenderContext(root, context, context.Render<Settings>(), githubSettings, tasks, inbox);
    }

    /// <summary>Records the rename it is asked for and answers with the count the
    /// test chose; every other member is out of this page's reach.</summary>
    private sealed class RecordingTaskItems(int moved, bool refuse) : ITaskItems
    {
        public List<(string OldId, string NewId)> Renames { get; } = [];

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default)
        {
            Renames.Add((oldId, newId));
            return Task.FromResult(refuse
                ? Result.Failure<int>(Error.Unexpected("tasks.rename", "Entries could not follow the rename."))
                : Result.Success(moved));
        }

        public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItemDto>>([]);

        public Task<Result<SavedTaskDto>> SaveFromTextAsync(Guid? id, string rawText, int order, string? sourceInboxId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<TaskItemDto>> LinkToIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(0));

        public Task<Result<ImportPlanResultDto>> ImportPlanAsync(string rawText, string? defaultRepo = null, IReadOnlyDictionary<string, string>? repoMatches = null, string? sourceInboxId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingInboxItems(int moved) : IInboxItems
    {
        public List<(string OldId, string NewId)> Renames { get; } = [];

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default)
        {
            Renames.Add((oldId, newId));
            return Task.FromResult(Result.Success(moved));
        }

        public (bool Available, string? Reason) PlanDrafterAvailability => (false, null);
        public Task<InboxSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<InboxItemDto>> CaptureAsync(string title, string? notes = null, string channel = "manual", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> SetTagsAsync(Guid id, IReadOnlyList<string> tags, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> AssignRepositoriesAsync(Guid id, IReadOnlyList<string> repoIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> MoveToListAsync(Guid id, Guid? listId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> ArchiveAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<InboxRoutedDto>> RouteToBacklogAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<InboxRoutedDto>> CreatePlanAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<InboxListDto>> CreateListAsync(string name, Guid? groupId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> RenameListAsync(Guid listId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> DeleteListAsync(Guid listId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> MoveListToGroupAsync(Guid listId, Guid? groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<InboxGroupDto>> CreateGroupAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> RenameGroupAsync(Guid groupId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> UngroupAsync(Guid groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task EnsureDefaultOrganizerAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
        IRenderedComponent<Settings> Component,
        GitHubSettingsStore GitHub,
        RecordingTaskItems Tasks,
        RecordingInboxItems Inbox) : IDisposable
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
