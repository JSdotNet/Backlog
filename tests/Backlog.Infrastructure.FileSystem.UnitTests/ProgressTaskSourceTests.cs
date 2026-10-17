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
/// place the two meet. It answers the ids a goal names in any repository, and a linked
/// plan's tasks only in the asked repositories, finished or not.
/// </summary>
public class ProgressTaskSourceTests
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

    private static IReadOnlyList<ProgressTaskDto> Select(
        IReadOnlyList<TaskItemDto> backlog,
        ProgressTaskQuery query) =>
        ProgressTaskSource.Select(backlog, query, [BacklogRepository, ToolsRepository]);

    [Fact]
    public void A_linked_plan_brings_in_its_tasks_in_the_asked_repositories_finished_or_not()
    {
        var open = Entry("Open", tags: ["+goals-standup"]);
        var done = Entry("Done", EntryStatus.Done, tags: ["+goals-standup"], completedOn: new DateOnly(2026, 10, 12));
        var elsewhere = Entry("Elsewhere", tags: ["+goals-standup"], repoId: "JSdotNet/Tools");
        var otherPlan = Entry("Other plan", tags: ["+other"]);

        var read = Select([open, done, elsewhere, otherPlan], new ProgressTaskQuery([], ["+goals-standup"], ["backlog"]));

        Assert.Equal([open.Id, done.Id], read.Select(task => task.Id));
    }

    /// <summary>A goal's attached plan counts its entries in every repository
    /// (<c>.devbook/domain/progress/domain.md#goal-attachment</c>).</summary>
    [Fact]
    public void A_goals_plan_brings_in_its_tasks_in_every_repository()
    {
        var here = Entry("Here", tags: ["+bank-integration"]);
        var elsewhere = Entry("Elsewhere", tags: ["+bank-integration"], repoId: "JSdotNet/Tools");
        var unregistered = Entry("Unregistered", tags: ["+bank-integration"], repoId: "Someone/Else");

        var read = Select([here, elsewhere, unregistered], new ProgressTaskQuery([], ["bank-integration"], [], AnyRepository: true));

        Assert.Equal([here.Id, elsewhere.Id, unregistered.Id], read.Select(task => task.Id));
    }

    [Fact]
    public void A_task_carries_the_day_it_started_and_the_day_it_was_blocked()
    {
        var entry = Entry("Started", EntryStatus.InProgress, blockedSince: new DateOnly(2026, 10, 9)) with
        {
            StartedOn = new DateOnly(2026, 10, 7)
        };

        var task = Assert.Single(Select([entry], new ProgressTaskQuery([entry.Id], [], ["backlog"])));

        Assert.Equal(new DateOnly(2026, 10, 7), task.StartedOn);
        Assert.Equal(new DateOnly(2026, 10, 9), task.BlockedSince);
    }

    [Fact]
    public void A_task_named_by_id_is_read_in_whatever_repository_it_lives()
    {
        var elsewhere = Entry("Elsewhere", repoId: "JSdotNet/Tools");

        var read = Select([elsewhere, Entry("Unnamed")], new ProgressTaskQuery([elsewhere.Id], [], ["backlog"]));

        var task = Assert.Single(read);
        Assert.Equal(["tools"], task.RepositoryAliases);
    }

    [Fact]
    public void A_task_reads_in_progress_words_with_its_plan_block_and_pull_requests()
    {
        var entry = Entry(
            "Model",
            EntryStatus.InProgress,
            tags: ["+Goals-Standup", "#release"],
            blockedSince: new DateOnly(2026, 10, 13),
            projections:
            [
                new EntryProjectionDto("JSdotNet/Backlog", "1210", EntryProjectionDto.PullRequestTargetType),
                new EntryProjectionDto("JSdotNet/Backlog", "88", EntryProjectionDto.IssueTargetType),
                new EntryProjectionDto("JSdotNet/Backlog", "session-1", EntryProjectionDto.SessionTargetType)
            ]);

        var task = Assert.Single(Select([entry], new ProgressTaskQuery([entry.Id], [], [])));

        Assert.Equal(ProgressTaskStatus.InProgress, task.Status);
        Assert.True(task.IsBlocked);
        Assert.Equal(["Goals-Standup"], task.PlanTags);
        Assert.Equal(["+Goals-Standup", "#release"], task.Tags);
        Assert.Equal(["backlog"], task.RepositoryAliases);
        Assert.Equal(new DateOnly(2026, 10, 1), task.CreatedOn);
        var pull = Assert.Single(task.PullRequests);
        Assert.Equal(("JSdotNet/Backlog", "1210"), (pull.Repository, pull.Number));
    }

    [Fact]
    public void A_task_carries_its_due_day_effort_and_order()
    {
        var entry = Entry("Recurring invoices") with { DueOn = new DateOnly(2026, 10, 22), Effort = 5, Order = 7 };

        var task = Assert.Single(Select([entry], new ProgressTaskQuery([entry.Id], [], [])));

        Assert.Equal((new DateOnly(2026, 10, 22), 5, 7), (task.DueOn, task.Effort, task.Order));
        Assert.False(task.WaitsOnOpenWork);
    }

    /// <summary>The stand-up reads the tasks finished in its history and due in its next
    /// window, whichever goal holds them or none, in any repository.</summary>
    [Fact]
    public void The_completion_and_due_ranges_select_tasks_in_any_repository()
    {
        var finished = Entry("VAT rate per line item", EntryStatus.Done, repoId: "Someone/Else", completedOn: new DateOnly(2026, 10, 14));
        var finishedBefore = Entry("Earlier", EntryStatus.Done, completedOn: new DateOnly(2026, 9, 1));
        var due = Entry("Recurring invoices") with { DueOn = new DateOnly(2026, 10, 22) };
        var dueLater = Entry("Later") with { DueOn = new DateOnly(2026, 11, 2) };

        var read = Select(
            [finished, finishedBefore, due, dueLater],
            new ProgressTaskQuery(
                [],
                [],
                [],
                CompletedFrom: new DateOnly(2026, 9, 21),
                CompletedThrough: new DateOnly(2026, 10, 16),
                DueFrom: new DateOnly(2026, 10, 19),
                DueThrough: new DateOnly(2026, 10, 23)));

        Assert.Equal([finished.Id, due.Id], read.Select(task => task.Id));
    }

    [Fact]
    public void Without_ranges_no_task_is_selected_by_its_days()
    {
        var finished = Entry("Done", EntryStatus.Done, completedOn: new DateOnly(2026, 10, 14)) with { DueOn = new DateOnly(2026, 10, 14) };

        Assert.Empty(Select([finished], new ProgressTaskQuery([], [], [])));
    }

    /// <summary>An entry waits on open work while one of its <c>after:</c> values — a real id
    /// or an import slug — names a task not done, or names no live task.</summary>
    [Fact]
    public void A_task_waits_on_open_work_while_a_dependency_is_not_done()
    {
        var done = Entry("Bank import mapping", EntryStatus.Done, completedOn: new DateOnly(2026, 10, 12)) with { ImportItemId = "mapping", ImportPlanId = "bank-integration" };
        var open = Entry("Payment matching rules") with { ImportItemId = "rules", ImportPlanId = "bank-integration" };
        var afterDone = Entry("After done", tags: ["+bank-integration"]) with { DependsOn = ["mapping"], ImportPlanId = "bank-integration" };
        var afterOpen = Entry("After open") with { DependsOn = [open.Id.ToString()] };
        var afterNothing = Entry("After nothing") with { DependsOn = ["no-such-step"] };
        var completedNotDone = Entry("Completed", EntryStatus.Ready, completedOn: new DateOnly(2026, 10, 12));
        var afterCompleted = Entry("After completed") with { DependsOn = [completedNotDone.Id.ToString()] };

        var read = Select(
            [done, open, afterDone, afterOpen, afterNothing, completedNotDone, afterCompleted],
            new ProgressTaskQuery([afterDone.Id, afterOpen.Id, afterNothing.Id, afterCompleted.Id], [], []));

        Assert.Equal(
            [("After done", false), ("After open", true), ("After nothing", true), ("After completed", false)],
            read.Select(task => (task.Title, task.WaitsOnOpenWork)));
    }

    [Fact]
    public async Task It_reads_the_backlog_through_the_published_port_and_writes_nothing()
    {
        var entry = Entry("Only", tags: ["+plan"]);
        var tasks = new ListOnlyTaskItems([entry]);
        var source = new ProgressTaskSource(() => tasks, () => null);

        var read = await source.ReadAsync(new ProgressTaskQuery([], ["plan"], ["JSdotNet/Backlog"]), TestContext.Current.CancellationToken);

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

        public Task<Result<int>> RefileRepositoryTasksAsync(string repositoryId, string? moveToId, bool drop, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
