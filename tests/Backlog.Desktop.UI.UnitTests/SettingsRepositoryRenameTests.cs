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
/// A repository renamed on GitHub, applied from its card in Settings. The store
/// carries its own row and records the move; what this page owes on top is the
/// call into each module that holds entries filed against the old id, and a
/// sentence on the card saying what it did. Asserted against the recorded calls
/// and the card, because a page that saved the new id and told nobody would
/// leave every entry pointing at a coordinate until the next start's pass.
/// </summary>
public sealed class SettingsRepositoryRenameTests
{
    [Fact]
    public void Renaming_from_the_card_re_points_entries_and_inbox_items_and_says_so()
    {
        using var settings = RenderSettings(entriesMoved: 12, itemsMoved: 1);
        OpenRepositoriesTab(settings.Component);

        Rename(settings.Component, "JSdotNet/Backlog-renamed");

        Assert.Equal([("JSdotNet/Backlog", "JSdotNet/Backlog-renamed")], settings.Tasks.Renames);
        Assert.Equal([("JSdotNet/Backlog", "JSdotNet/Backlog-renamed")], settings.Inbox.Renames);
        Assert.Equal(
            "Renamed JSdotNet/Backlog to JSdotNet/Backlog-renamed; 12 entries and 1 inbox item followed it.",
            settings.Component.Find("[data-testid='repo-rename-note']").TextContent.Trim());
        Assert.Equal("JSdotNet/Backlog-renamed", settings.Component.Find(".repo-card__title").TextContent.Trim());
        Assert.Equal("JSdotNet/Backlog-renamed", settings.GitHub.Current.Find("backlog")!.FullName);
        Assert.Empty(settings.Component.FindAll("[data-testid='repo-rename']"));
        Assert.Empty(settings.Component.FindAll(".setting__status--error"));
    }

    /// <summary>
    /// Renaming onto a repository that is already configured folds the renamed
    /// one into it — how a placeholder a plan import registered is merged into
    /// the real repository. The renamed card is gone, so the note lands on the
    /// card that was kept, and that card is the one left open.
    /// </summary>
    [Fact]
    public void Renaming_onto_a_configured_repository_merges_into_it_and_says_so_on_its_card()
    {
        using var settings = RenderSettings(entriesMoved: 3);
        OpenRepositoriesTab(settings.Component);

        Rename(settings.Component, "JSdotNet/Docs");

        Assert.Equal([("JSdotNet/Backlog", "JSdotNet/Docs")], settings.Tasks.Renames);
        Assert.Equal([("JSdotNet/Backlog", "JSdotNet/Docs")], settings.Inbox.Renames);
        Assert.Equal("JSdotNet/Docs", Assert.Single(settings.GitHub.Current.Repositories).FullName);
        Assert.Equal(
            "Merged JSdotNet/Backlog into JSdotNet/Docs; 3 entries and 0 inbox items followed it.",
            settings.Component.Find("[data-testid='repo-rename-note']").TextContent.Trim());
        Assert.Equal("JSdotNet/Docs", settings.Component.Find(".repo-card__title").TextContent.Trim());
        Assert.Empty(settings.Component.FindAll(".setting__status--error"));
    }

    /// <summary>The Rename control sits before Remove: the one that keeps
    /// everything, then the one that keeps nothing.</summary>
    [Fact]
    public void Rename_sits_before_Remove_on_the_card()
    {
        using var settings = RenderSettings();
        OpenRepositoriesTab(settings.Component);

        var buttons = settings.Component.FindAll(".repo-card__actions button");

        Assert.Equal(["Rename", "Remove repository"], buttons.Select(button => button.TextContent.Trim()));
    }

    /// <summary>A refusal from the store is the form's to show and leaves the
    /// form open with nothing renamed: a name to correct, not a rename that
    /// half happened.</summary>
    [Fact]
    public void A_name_that_is_not_a_coordinate_is_refused_on_the_form_and_renames_nothing()
    {
        using var settings = RenderSettings();
        OpenRepositoriesTab(settings.Component);

        Rename(settings.Component, "not-a-repository");

        Assert.Equal(GitHubSettingsStore.RenameNotACoordinate, settings.Component.Find("[data-testid='repo-rename-status'] .setting__status--error").TextContent.Trim());
        Assert.NotEmpty(settings.Component.FindAll("[data-testid='repo-rename']"));
        Assert.Empty(settings.Tasks.Renames);
        Assert.Equal("JSdotNet/Backlog", settings.GitHub.Current.Find("backlog")!.FullName);
    }

    /// <summary>A module that refuses is reported, not swallowed: the registry has
    /// moved on and an entry left behind is the one thing to know about.</summary>
    [Fact]
    public void A_module_that_refuses_is_reported_in_the_status_line()
    {
        using var settings = RenderSettings(tasksRefuse: true);
        OpenRepositoriesTab(settings.Component);

        Rename(settings.Component, "JSdotNet/Backlog-renamed");

        Assert.Contains("could not follow", settings.Component.Find(".setting__status--error").TextContent, StringComparison.Ordinal);
        Assert.Equal("JSdotNet/Backlog-renamed", settings.GitHub.Current.Find("backlog")!.FullName);
    }

    /// <summary>
    /// The text box does not rename. Changing a line's <c>owner/name</c> there
    /// is a removed repository beside a new one, so neither module is called
    /// and nothing is recorded — and the one edit that reads like a rename earns
    /// a sentence pointing at the control that is one.
    /// </summary>
    [Fact]
    public void Retyping_an_owner_name_in_the_list_renames_nothing_and_points_at_Rename()
    {
        using var settings = RenderSettings();
        OpenRepositoriesTab(settings.Component);

        Retype(settings.Component, "backlog = JSdotNet/Backlog-renamed\ndocs = JSdotNet/Docs");

        Assert.Empty(settings.Tasks.Renames);
        Assert.Empty(settings.Inbox.Renames);
        Assert.Empty(settings.GitHub.Current.Renames);
        Assert.Contains("use Rename on its card", settings.Component.Find("[data-testid='github-repos-replaced-hint']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Relabelling_an_alias_calls_neither_module_and_shows_no_hint()
    {
        using var settings = RenderSettings();
        OpenRepositoriesTab(settings.Component);

        Retype(settings.Component, "bl = JSdotNet/Backlog\ndocs = JSdotNet/Docs");

        Assert.Empty(settings.Tasks.Renames);
        Assert.Empty(settings.Inbox.Renames);
        Assert.Empty(settings.Component.FindAll("[data-testid='github-repos-replaced-hint']"));
    }

    private static void Rename(IRenderedComponent<Settings> component, string newName)
    {
        component.Find("[data-testid='rename-repository-button']").Click();
        component.Find("[data-testid='repo-rename-input']").Input(newName);
        component.Find("[data-testid='repo-rename-apply']").Click();
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

        public Task<Result<ImportPlanResultDto>> ImportPlanAsync(string rawText, string? defaultRepo = null, IReadOnlyDictionary<string, string>? repoMatches = null, string? sourceInboxId = null, bool layOutOnRoadmap = false, CancellationToken cancellationToken = default) =>
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
