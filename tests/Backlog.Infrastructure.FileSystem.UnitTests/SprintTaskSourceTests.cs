using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// Progress reads tasks through Tasks' published surface only: this adapter is the one
/// place the two meet. It answers the ids a sprint names in any repository, and a linked
/// plan's tasks only in the series' repositories, finished or not.
/// </summary>
public class SprintTaskSourceTests
{
    private static readonly TasksRepositoryRef BacklogRepository = new("backlog", "JSdotNet", "Backlog");
    private static readonly TasksRepositoryRef ToolsRepository = new("tools", "JSdotNet", "Tools");

    private static TaskItemDto Entry(
        string title,
        EntryStatus status = EntryStatus.Ready,
        string[]? tags = null,
        string repoId = "JSdotNet/Backlog",
        DateOnly? blockedSince = null,
        DateOnly? completedOn = null,
        IReadOnlyList<EntryProjectionDto>? projections = null) =>
        new(
            Guid.NewGuid(),
            title,
            Body: string.Empty,
            EntryType.Prompt,
            Priority.Medium,
            status,
            Area: null,
            tags ?? [],
            Order: 0,
            TotalSubItems: 0,
            CompletedSubItems: 0,
            projections ?? [],
            CompletedOn: completedOn,
            RepoIds: [repoId],
            CreatedAt: new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
            BlockedSince: blockedSince);

    private static IReadOnlyList<SprintTaskDto> Select(
        IReadOnlyList<TaskItemDto> backlog,
        SprintTaskQuery query) =>
        SprintTaskSource.Select(backlog, query, [BacklogRepository, ToolsRepository]);

    [Fact]
    public void A_linked_plan_brings_in_its_tasks_in_the_series_repositories_finished_or_not()
    {
        var open = Entry("Open", tags: ["+progress-sprints"]);
        var done = Entry("Done", EntryStatus.Done, tags: ["+progress-sprints"], completedOn: new DateOnly(2026, 10, 12));
        var elsewhere = Entry("Elsewhere", tags: ["+progress-sprints"], repoId: "JSdotNet/Tools");
        var otherPlan = Entry("Other plan", tags: ["+other"]);

        var read = Select([open, done, elsewhere, otherPlan], new SprintTaskQuery([], ["+progress-sprints"], ["backlog"]));

        Assert.Equal([open.Id, done.Id], read.Select(task => task.Id));
    }

    [Fact]
    public void A_task_carries_the_day_it_started_and_the_day_it_was_blocked()
    {
        var entry = Entry("Started", EntryStatus.InProgress, blockedSince: new DateOnly(2026, 10, 9)) with
        {
            StartedOn = new DateOnly(2026, 10, 7)
        };

        var task = Assert.Single(Select([entry], new SprintTaskQuery([entry.Id], [], ["backlog"])));

        Assert.Equal(new DateOnly(2026, 10, 7), task.StartedOn);
        Assert.Equal(new DateOnly(2026, 10, 9), task.BlockedSince);
    }

    [Fact]
    public void A_task_named_by_id_is_read_in_whatever_repository_it_lives()
    {
        var elsewhere = Entry("Elsewhere", repoId: "JSdotNet/Tools");

        var read = Select([elsewhere, Entry("Unnamed")], new SprintTaskQuery([elsewhere.Id], [], ["backlog"]));

        var task = Assert.Single(read);
        Assert.Equal(["tools"], task.RepositoryAliases);
    }

    [Fact]
    public void A_task_reads_in_progress_words_with_its_plan_block_and_pull_requests()
    {
        var entry = Entry(
            "Model",
            EntryStatus.InProgress,
            tags: ["+Progress-Sprints", "#release"],
            blockedSince: new DateOnly(2026, 10, 13),
            projections:
            [
                new EntryProjectionDto("JSdotNet/Backlog", "1210", EntryProjectionDto.PullRequestTargetType),
                new EntryProjectionDto("JSdotNet/Backlog", "88", EntryProjectionDto.IssueTargetType),
                new EntryProjectionDto("JSdotNet/Backlog", "session-1", EntryProjectionDto.SessionTargetType)
            ]);

        var task = Assert.Single(Select([entry], new SprintTaskQuery([entry.Id], [], [])));

        Assert.Equal(SprintTaskStatus.InProgress, task.Status);
        Assert.True(task.IsBlocked);
        Assert.Equal(["Progress-Sprints"], task.PlanTags);
        Assert.Equal(["+Progress-Sprints", "#release"], task.Tags);
        Assert.Equal(["backlog"], task.RepositoryAliases);
        Assert.Equal(new DateOnly(2026, 10, 1), task.CreatedOn);
        var pull = Assert.Single(task.PullRequests);
        Assert.Equal(("JSdotNet/Backlog", "1210"), (pull.Repository, pull.Number));
    }

    [Fact]
    public async Task It_reads_the_backlog_through_the_published_port_and_writes_nothing()
    {
        var entry = Entry("Only", tags: ["+plan"]);
        var tasks = new ListOnlyTaskItems([entry]);
        var source = new SprintTaskSource(() => tasks, () => null);

        var read = await source.ReadAsync(new SprintTaskQuery([], ["plan"], ["JSdotNet/Backlog"]), TestContext.Current.CancellationToken);

        Assert.Equal([entry.Id], read.Select(task => task.Id));
        Assert.Equal(1, tasks.Lists);
    }

    /// <summary>The backlog's port, answering its list and refusing every write, so a
    /// write this adapter made would fail the test rather than pass unnoticed.</summary>
    private sealed class ListOnlyTaskItems(IReadOnlyList<TaskItemDto> entries) : ITaskItems
    {
        public int Lists { get; private set; }

        public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default)
        {
            Lists++;
            return Task.FromResult(entries);
        }

        public Task<Result<SavedTaskDto>> SaveFromTextAsync(Guid? id, string rawText, int order, string? sourceInboxId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<ImportPlanResultDto>> ImportPlanAsync(string rawText, string? defaultRepo = null, IReadOnlyDictionary<string, string>? repoMatches = null, string? sourceInboxId = null, IReadOnlyDictionary<string, string>? sourceInboxIds = null, bool layOutOnRoadmap = false, CancellationToken cancellationToken = default) =>
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

        public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
