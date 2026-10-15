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
        OpenRepositoryCard(settings.Component);

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
        OpenRepositoryCard(settings.Component);

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
        OpenRepositoryCard(settings.Component);

        var buttons = settings.Component.FindAll(".repo-card__actions button");

        Assert.Equal(["Rename", "Remove repository"], buttons.Select(button => button.TextContent.Trim()));
    }

    /// <summary>
    /// A registry that arrives with two rows sharing an alias — a placeholder a plan
    /// registered beside the real repository — still opens the page with a row each.
    /// Rows keyed by the alias threw Blazor's duplicate-key exception and the whole
    /// page showed the unhandled-error banner, so they are keyed by the id.
    /// </summary>
    [Fact]
    public void A_registry_with_a_shared_alias_opens_the_page_with_a_row_each()
    {
        using var settings = RenderSettings(registry: """
            {
              "repositories": [
                { "id": "finance/finance", "alias": "finance" },
                { "id": "JSdotNet/finance", "alias": "finance" }
              ]
            }
            """);

        SettingsMenu.Open(settings.Component, "Repositories");

        Assert.Equal(2, settings.Component.FindAll("[data-testid='repo-edit']").Count);
    }

    /// <summary>A refusal from the store is the form's to show and leaves the
    /// form open with nothing renamed: a name to correct, not a rename that
    /// half happened.</summary>
    [Fact]
    public void A_name_that_is_not_a_coordinate_is_refused_on_the_form_and_renames_nothing()
    {
        using var settings = RenderSettings();
        OpenRepositoryCard(settings.Component);

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
        OpenRepositoryCard(settings.Component);

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
        OpenRepositoryCard(settings.Component);

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
        OpenRepositoryCard(settings.Component);

        Retype(settings.Component, "bl = JSdotNet/Backlog\ndocs = JSdotNet/Docs");

        Assert.Empty(settings.Tasks.Renames);
        Assert.Empty(settings.Inbox.Renames);
        Assert.Empty(settings.Component.FindAll("[data-testid='github-repos-replaced-hint']"));
    }

    /// <summary>
    /// Removing the repository whose row is open leaves the other one's row standing,
    /// closed, rather than handing its card the removed one's settings: rows are keyed
    /// by the repository, so what is left is the docs row and nothing of the
    /// backlog card stays open under it.
    /// </summary>
    [Fact]
    public void Removing_the_open_repository_closes_its_card_and_keeps_the_other_row()
    {
        using var settings = RenderSettings();
        OpenRepositoryCard(settings.Component);

        settings.Component.Find("[data-testid='remove-repository-button']").Click();

        var edit = Assert.Single(settings.Component.FindAll("[data-testid='repo-edit']"));
        Assert.Equal("Edit docs", edit.GetAttribute("aria-label"));
        Assert.Equal("false", edit.GetAttribute("aria-expanded"));
        Assert.Empty(settings.Component.FindAll("[data-testid='github-repo-card']"));

        SettingsMenu.EditRepository(settings.Component, "docs");

        var card = settings.Component.Find("[data-testid='github-repo-card']");
        Assert.Equal("JSdotNet/Docs repository settings", card.GetAttribute("aria-label"));
    }

    /// <summary>
    /// The same, with the first repository removed the other way: by saving the
    /// repositories text without its line. What is left on screen is the second
    /// repository's own card, open under its own row, carrying its own alias and its
    /// own clone directory rather than the removed one's, so nothing the first card
    /// held leaks into the card that survives it.
    /// </summary>
    [Fact]
    public async Task Removing_the_first_repository_from_the_text_keeps_the_second_card_bound_to_its_own_settings()
    {
        using var settings = RenderSettings();
        OpenRepositoryCard(settings.Component);
        var backlogClone = Path.Combine(settings.Root, "clones", "backlog");
        var docsClone = Path.Combine(settings.Root, "clones", "docs");

        await SetCloneDirectory(settings.Component, backlogClone);
        SettingsMenu.EditRepository(settings.Component, "docs");
        await SetCloneDirectory(settings.Component, docsClone);

        // On the renderer's thread in one go: the clone folders typed above are
        // checked in the background, and an answer landing between the typing
        // and the change would re-render the box under the test.
        await settings.Component.InvokeAsync(() => Retype(settings.Component, "docs = JSdotNet/Docs"));

        Assert.Equal("JSdotNet/Docs", Assert.Single(settings.GitHub.Current.Repositories).FullName);
        var edit = Assert.Single(settings.Component.FindAll("[data-testid='repo-edit']"));
        Assert.Equal("true", edit.GetAttribute("aria-expanded"));

        var card = settings.Component.Find("[data-testid='github-repo-card']");
        Assert.Equal("JSdotNet/Docs repository settings", card.GetAttribute("aria-label"));
        Assert.Equal("JSdotNet/Docs", card.QuerySelector(".repo-card__title")!.TextContent.Trim());
        Assert.Contains("@docs", card.QuerySelector(".repo-card__meta")!.TextContent, StringComparison.Ordinal);

        var clone = card.QuerySelector("[data-testid='repo-clone-directory-input']")!;
        Assert.Equal("clone-docs", clone.GetAttribute("id"));
        Assert.Equal(docsClone, clone.GetAttribute("value"));
        Assert.DoesNotContain(backlogClone, card.TextContent, StringComparison.Ordinal);
    }

    /// <summary>Types a clone folder and leaves the field. Each step is found and
    /// raised on the renderer's own thread, and the card is let settle after,
    /// because the folder typed is checked with git in the background and its
    /// answer re-renders the card - between a Find and its event, that would
    /// leave the event pointing at a handler the render replaced.</summary>
    private static async Task SetCloneDirectory(IRenderedComponent<Settings> component, string directory)
    {
        await component.InvokeAsync(() => component.Find("[data-testid='repo-clone-directory-input']").Input(directory));
        await component.InvokeAsync(() => component.Find("[data-testid='repo-clone-directory-input']").Change(directory));
        component.WaitForAssertion(() =>
        {
            var status = component.Find("[data-testid='repo-clone-status']").TextContent;
            Assert.False(string.IsNullOrWhiteSpace(status));
            Assert.DoesNotContain("Checking the local clone", status, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A rename keeps the alias and the clone folder and changes the id, and the
    /// clone's status hangs on the id: a clone is "ready" only when its origin
    /// is the repository the row names. So the status the card had for the old
    /// name is not kept for the new one - it is asked again.
    /// </summary>
    [Fact]
    public async Task A_rename_asks_about_the_clone_again_rather_than_keeping_the_old_names_answer()
    {
        var git = new GatedLocalGit();
        using var settings = RenderSettings(localGit: git);
        var clone = Path.Combine(settings.Root, "clones", "backlog");
        git.Cloned(clone, "JSdotNet/Backlog");
        OpenRepositoryCard(settings.Component);
        await SetCloneDirectory(settings.Component, clone);
        settings.Component.WaitForAssertion(() => Assert.Contains(
            "Local clone:", settings.Component.Find("[data-testid='repo-clone-status']").TextContent, StringComparison.Ordinal));

        Rename(settings.Component, "JSdotNet/Backlog-next");

        settings.Component.WaitForAssertion(() =>
        {
            var status = settings.Component.Find("[data-testid='repo-clone-status']").TextContent;
            Assert.Contains("its origin is not JSdotNet/Backlog-next", status, StringComparison.Ordinal);
            Assert.DoesNotContain("Local clone:", status, StringComparison.Ordinal);
            Assert.Equal("Clone repository", settings.Component.Find("[data-testid='repo-clone-button']").TextContent.Trim());
        });
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

    private static void OpenRepositoryCard(IRenderedComponent<Settings> component) =>
        SettingsMenu.OpenRepository(component);

    private static SettingsRenderContext RenderSettings(int entriesMoved = 0, int itemsMoved = 0, bool tasksRefuse = false, string? registry = null, ILocalGitRepositoryService? localGit = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-rename-tests", Guid.NewGuid().ToString("n"));

        if (registry is not null)
        {
            var registryPath = Path.Combine(root, "workspace", GitHubSettingsStore.RegistryFolderName, "repos.json");
            Directory.CreateDirectory(Path.GetDirectoryName(registryPath)!);
            File.WriteAllText(registryPath, registry);
        }

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"), () => Path.Combine(root, "workspace"));
        if (registry is null)
        {
            var (repositories, errors) = GitHubSettings.ParseText("backlog = JSdotNet/Backlog\ndocs = JSdotNet/Docs");
            Assert.Empty(errors);
            Assert.Null(githubSettings.SetRepositories(repositories));
        }

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
        context.Services.AddSingleton(localGit ?? new LocalGitRepositoryService());
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

    private sealed class RecordingInboxItems(int moved) : IInboxItems
    {
        public List<(string OldId, string NewId)> Renames { get; } = [];

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default)
        {
            Renames.Add((oldId, newId));
            return Task.FromResult(Result.Success(moved));
        }

        public (bool Available, string? Reason) PlanDrafterAvailability => (false, null);

        public bool TriageAdvisorAvailable => false;

        public Task<Result<InboxTriageAdviceDto>> AdviseTriageAsync(Guid id, IReadOnlyList<string>? repositories = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<InboxTriagePassDto>> ProposeTriagePassAsync(IReadOnlyList<Guid> ids, IReadOnlyList<string>? repositories = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<InboxSuggestionDto>>> SuggestAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success<IReadOnlyList<InboxSuggestionDto>>([]));

        public Task<Result> DismissSuggestionAsync(Guid id, string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
        public Task<InboxSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<InboxItemDto>> CaptureAsync(string title, string? notes = null, string channel = "manual", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> SetTagsAsync(Guid id, IReadOnlyList<string> tags, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> AssignRepositoriesAsync(Guid id, IReadOnlyList<string> repoIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> MoveToListAsync(Guid id, Guid? listId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> EditNoteAsync(Guid id, string title, string? bodyMd, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> ArchiveAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> ArchiveAsDuplicateAsync(Guid id, Guid duplicateOf, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> LinkToTaskAsync(Guid id, Guid taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> MergeIntoTaskAsync(Guid id, Guid taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<InboxRelationsDto>> RelatedAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(InboxRelationsDto.None(id)));
        public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> DeferAsync(Guid id, DateOnly? until, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> ResurfaceAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> RestoreAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> ReturnToInboxAsync(Guid id, bool deleteTasks, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<int>> ResurfaceDueAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<InboxBatchResultDto> SetTagsAsync(IReadOnlyDictionary<Guid, IReadOnlyList<string>> tagsByItem, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InboxBatchResultDto> AssignRepositoriesAsync(IReadOnlyList<Guid> ids, IReadOnlyList<string> repoIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InboxBatchResultDto> MoveToListAsync(IReadOnlyList<Guid> ids, Guid? listId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InboxBatchResultDto> ArchiveAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InboxBatchResultDto> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<InboxRoutedDto>> RouteToBacklogAsync(Guid id, string? entryType = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<InboxBatchRoutedDto>> RouteToBacklogAsync(IReadOnlyList<Guid> ids, Guid? listId = null, InboxBatchRouteChoicesDto? choices = null, string? planTitle = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<InboxBatchProposalDto>> ProposeBatchAsync(IReadOnlyList<Guid> ids, Guid? listId = null, string? planTitle = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<ProposedDependency>>> InferBatchOrderAsync(IReadOnlyList<Guid> ids, string planTag, IReadOnlyDictionary<Guid, IReadOnlyList<string>>? repositories = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<InboxRoutedDto>> CreatePlanAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<InboxBrainRoutedDto>> RouteToBrainAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<InboxListDto>> CreateListAsync(string name, Guid? groupId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> RenameListAsync(Guid listId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> DeleteListAsync(Guid listId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> MoveListToGroupAsync(Guid listId, Guid? groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<InboxGroupDto>> CreateGroupAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> RenameGroupAsync(Guid groupId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> UngroupAsync(Guid groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task EnsureDefaultOrganizerAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> RetryAttachmentAsync(Guid id, Guid attachmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<byte[]>> ReadAttachmentAsync(Guid id, Guid attachmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> OpenAttachmentAsync(Guid id, Guid attachmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
