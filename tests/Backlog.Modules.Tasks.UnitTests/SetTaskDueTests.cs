using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.SetTaskDue;
using Backlog.Modules.Tasks.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// Setting or clearing a task's due date as a use case of its own (local ADR 0056 §2): the
/// write a day dragged on the Calendar asks for, without the Calendar saving the task's
/// text. Only the due date moves.
/// </summary>
public sealed class SetTaskDueTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);

    [Fact]
    public async Task The_due_date_is_set_and_answered()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Refactor sync", string.Empty, EntryType.Task);
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);
        store.Writes = 0;

        var result = await Handle(store, entry.Id, Monday);

        Assert.True(result.IsSuccess);
        Assert.Equal(Monday, result.Value.DueOn);
        Assert.Equal(Monday, store.Entries[entry.Id].DueOn);
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task No_day_clears_the_due_date()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Refactor sync", string.Empty, EntryType.Task);
        entry.SetDueOn(Monday);
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);

        var result = await Handle(store, entry.Id, null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.DueOn);
        Assert.Null(store.Entries[entry.Id].DueOn);
    }

    [Fact]
    public async Task A_missing_task_is_not_found_and_nothing_is_written()
    {
        var store = new InMemoryTaskRepository();

        var result = await Handle(store, Guid.NewGuid(), Monday);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, store.Writes);
    }

    /// <summary>A drag moves the day and nothing else: the title, the status, the repeat,
    /// the planned hours, the repositories and the Devbook references are as they were.</summary>
    [Fact]
    public async Task Every_other_field_is_left_as_it_was()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Weekly report", "The body.", EntryType.Task, repoIds: ["acme/app"]);
        entry.SetStatus(EntryStatus.InProgress, Monday);
        entry.SetRecurrence(new Recurrence(1, RecurrenceUnit.Week));
        entry.SetDueOn(Monday);
        entry.SetPlannedHours([PlannedHoursBlock.Create(Monday, 3m)]);
        entry.SetDevbookReferences([".devbook/domain/calendar/context.md"]);
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);
        var before = entry.ToDto();

        var result = await Handle(store, entry.Id, Monday.AddDays(3));

        Assert.Equal(before with { DueOn = Monday.AddDays(3) }, result.Value, new SameEntry());
    }

    /// <summary>A host or a double that keeps no due-date command still composes, and
    /// refuses the write in words rather than throwing.</summary>
    [Fact]
    public async Task A_port_with_no_due_date_command_refuses_without_throwing()
    {
        ITaskItems port = new ListOnlyTaskItems();

        var result = await port.SetDueAsync(Guid.NewGuid(), Monday, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("entry.due_unavailable", result.Error.Code);
    }

    private static Task<Result<TaskItemDto>> Handle(InMemoryTaskRepository store, Guid id, DateOnly? dueOn) =>
        new SetTaskDueCommandHandler(store)
            .Handle(new SetTaskDueCommand(id, dueOn), TestContext.Current.CancellationToken);

    /// <summary>Record equality compares the list members by reference, so the lists are
    /// compared by their items here.</summary>
    private sealed class SameEntry : IEqualityComparer<TaskItemDto>
    {
        public bool Equals(TaskItemDto? x, TaskItemDto? y) =>
            x is not null && y is not null
            && x.Title == y.Title
            && x.Body == y.Body
            && x.Status == y.Status
            && x.DueOn == y.DueOn
            && Equals(x.Recurrence, y.Recurrence)
            && x.CompletedOn == y.CompletedOn
            && (x.RepoIds ?? []).SequenceEqual(y.RepoIds ?? [])
            && x.PlannedHours.SequenceEqual(y.PlannedHours)
            && x.DevbookReferences.SequenceEqual(y.DevbookReferences)
            && x.Tags.SequenceEqual(y.Tags);

        public int GetHashCode(TaskItemDto obj) => obj.Id.GetHashCode();
    }

    // Differs from RecordingTaskItems: answers an empty list and throws on every write.
    private sealed class ListOnlyTaskItems : ITaskItems
    {
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

        public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> RefileRepositoryTasksAsync(string repositoryId, string? moveToId, bool drop, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<ImportPlanResultDto>> ImportPlanAsync(
            string rawText,
            string? defaultRepo = null,
            IReadOnlyDictionary<string, string>? repoMatches = null,
            string? sourceInboxId = null,
            IReadOnlyDictionary<string, string>? sourceInboxIds = null,
            bool layOutOnRoadmap = false,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
