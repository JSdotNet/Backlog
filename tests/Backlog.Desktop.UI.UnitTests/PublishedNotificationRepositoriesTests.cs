using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The repository the owner publishes with each notification it pushes (local ADR 0039 §6,
/// <c>requirements.md#requirement-the-owner-publishes-the-repository-a-notification-is-about</c>):
/// the centre's rule, run for the background exchange, a linked task read ahead of it.
/// </summary>
public sealed class PublishedNotificationRepositoriesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    private static readonly GitHubSettings Settings = new()
    {
        Repositories = [new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog"), new GitHubRepositoryRef("fincent", "JSdotNet", "Fincent")],
    };

    private static string? AliasOf(string? repository) => Settings.Find(repository)?.Alias;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static NotificationSnapshot Snapshot(NotificationSource source, NotificationSubject? subject = null, NotificationOrigin? origin = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), source, subject, NotificationKind.Question, NotificationSeverity.Info,
            "A question", string.Empty, string.Empty, null, null, false, Now, NotificationState.Open, null, null, null, origin);

    [Fact]
    public async Task A_runs_validation_question_is_published_under_its_clones_alias()
    {
        var source = new PublishedNotificationRepositories(AliasOf, worktrees: () => new Worktrees(@"D:\Repos\Backlog", "JSdotNet/Backlog"));
        var run = Snapshot(
            new NotificationSource("sessions", SessionNotificationSubjects.ValidationSourceKey("run-1", @"D:\Repos\Backlog")),
            new NotificationSubject("sessions", "run", "run-1"),
            new NotificationOrigin("Claude Code", "Backlog · The run"));

        var resolved = await source.ResolveAsync([run], Ct);

        Assert.Equal("backlog", resolved[run.Id]);
    }

    [Fact]
    public async Task A_pairing_question_is_published_as_the_brains()
    {
        var source = new PublishedNotificationRepositories(AliasOf);
        var pairing = Snapshot(new NotificationSource("sync", "pairing"), origin: new NotificationOrigin("Backlog", "Pairing"));

        var resolved = await source.ResolveAsync([pairing], Ct);

        Assert.True(resolved.ContainsKey(pairing.Id));
        Assert.Null(resolved[pairing.Id]);
    }

    [Fact]
    public async Task A_question_on_a_task_is_published_under_the_repository_the_task_is_filed_in()
    {
        var task = Guid.NewGuid();
        var source = new PublishedNotificationRepositories(
            AliasOf,
            taskFiling: (id, _) => Task.FromResult(id == task ? TaskFiling.Under("JSdotNet/Fincent") : TaskFiling.NotHeld));
        var question = Snapshot(
            new NotificationSource("spec-manager", "billing/q-1"),
            new NotificationSubject(TaskNotificationSubjects.Context, TaskNotificationSubjects.Task, TaskNotificationSubjects.Id(task)));

        var resolved = await source.ResolveAsync([question], Ct);

        Assert.Equal("fincent", resolved[question.Id]);
    }

    [Fact]
    public async Task A_task_that_could_not_be_read_leaves_its_question_not_known()
    {
        var source = new PublishedNotificationRepositories(
            AliasOf,
            taskFiling: (_, _) => throw new IOException("backlog.db is locked."));
        var question = Snapshot(
            new NotificationSource("spec-manager", "billing/q-1"),
            new NotificationSubject(TaskNotificationSubjects.Context, TaskNotificationSubjects.Task, TaskNotificationSubjects.Id(Guid.NewGuid())));

        var resolved = await source.ResolveAsync([question], Ct);

        Assert.False(resolved.ContainsKey(question.Id));
    }

    // --- As the desktop composes it, over the container --------------------------------

    private static NotificationSnapshot OnTask(Guid task) => Snapshot(
        new NotificationSource("spec-manager", "billing/q-1"),
        new NotificationSubject(TaskNotificationSubjects.Context, TaskNotificationSubjects.Task, TaskNotificationSubjects.Id(task)));

    private static PublishedNotificationRepositories Composed(params TaskItemDto[] tasks)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRepositoryDirectory>(new Directory());
        services.AddScoped<ITaskItems>(_ => new ListedTaskItems(tasks));
        return PublishedNotificationRepositories.Compose(services.BuildServiceProvider(validateScopes: true));
    }

    [Fact]
    public async Task Composed_a_question_on_a_task_is_published_under_the_tasks_repository_by_its_alias()
    {
        var task = Guid.NewGuid();
        var question = OnTask(task);

        var resolved = await Composed(Entry(task, "JSdotNet/Fincent")).ResolveAsync([question], Ct);

        Assert.Equal("fincent", resolved[question.Id]);
    }

    [Fact]
    public async Task Composed_a_question_on_a_task_the_backlog_does_not_hold_goes_to_the_other_rules()
    {
        // Not held: the spec-manager rule decides, and with no connected target it is the Brain's.
        var question = OnTask(Guid.NewGuid());

        var resolved = await Composed().ResolveAsync([question], Ct);

        Assert.True(resolved.ContainsKey(question.Id));
        Assert.Null(resolved[question.Id]);
    }

    [Fact]
    public async Task Composed_without_a_task_module_a_question_on_a_task_is_not_known()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRepositoryDirectory>(new Directory());
        var source = PublishedNotificationRepositories.Compose(services.BuildServiceProvider(validateScopes: true));
        var question = OnTask(Guid.NewGuid());

        var resolved = await source.ResolveAsync([question], Ct);

        Assert.False(resolved.ContainsKey(question.Id));
    }

    private static TaskItemDto Entry(Guid id, params string[] repoIds) =>
        new(id, "An entry", string.Empty, EntryType.Task, Priority.Medium, EntryStatus.Ready, null, [], 0, 0, 0, [], RepoIds: repoIds);

    private sealed class Directory : IRepositoryDirectory
    {
        public IReadOnlyList<TasksRepositoryRef> Repositories { get; } =
            [new("backlog", "JSdotNet", "Backlog"), new("fincent", "JSdotNet", "Fincent")];

        public TasksRepositoryRef? Resolve(string name) =>
            Repositories.FirstOrDefault(repository =>
                string.Equals(repository.Alias, name, StringComparison.Ordinal)
                || string.Equals($"{repository.Owner}/{repository.Name}", name, StringComparison.OrdinalIgnoreCase));

        public Result<TasksRepositoryRef> Register(string name) => throw new NotSupportedException();
    }

    /// <summary>The backlog as a list; only the read the push makes is answered.</summary>
    private sealed class ListedTaskItems(IReadOnlyList<TaskItemDto> tasks) : ITaskItems
    {
        public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(tasks);

        public Task<Result<int>> RefileRepositoryTasksAsync(string repositoryId, string? moveToId, bool drop, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<SavedTaskDto>> SaveFromTextAsync(Guid? id, string rawText, int order, string? sourceInboxId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<TaskItemDto>> LinkToIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<TaskItemDto>> UnlinkFromIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<TaskItemDto>> SetDevbookReferencesAsync(Guid id, IReadOnlyList<string> references, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ImportPlanResultDto>> ImportPlanAsync(string rawText, string? defaultRepo = null, IReadOnlyDictionary<string, string>? repoMatches = null, string? sourceInboxId = null, IReadOnlyDictionary<string, string>? sourceInboxIds = null, bool layOutOnRoadmap = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Worktrees(string folder, string repository) : ISessionRepositoryResolver
    {
        public string? RepositoryOf(string workingFolder) =>
            string.Equals(workingFolder, folder, StringComparison.OrdinalIgnoreCase) ? repository : null;
    }
}
