using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Features.CountTasks;
using Backlog.Modules.Tasks.Features.CreateTaskIfAbsent;
using Backlog.Modules.Tasks.Features.DeleteTask;
using Backlog.Modules.Tasks.Features.GetTask;
using Backlog.Modules.Tasks.Features.GetTaskVersion;
using Backlog.Modules.Tasks.Features.ImportPlan;
using Backlog.Modules.Tasks.Features.LinkTaskToIssue;
using Backlog.Modules.Tasks.Features.ListTasks;
using Backlog.Modules.Tasks.Features.ReconcileRepositoryIds;
using Backlog.Modules.Tasks.Features.RefileRepositoryTasks;
using Backlog.Modules.Tasks.Features.RenameRepository;
using Backlog.Modules.Tasks.Features.RecordTaskUsage;
using Backlog.Modules.Tasks.Features.ReorderTasks;
using Backlog.Modules.Tasks.Features.SaveTaskFromText;
using Backlog.Modules.Tasks.Features.SetDevbookReferences;
using Backlog.Modules.Tasks.Features.SetPlannedHours;
using Backlog.Modules.Tasks.Features.SetTaskDue;
using Backlog.Modules.Tasks.Features.UnlinkTaskFromIssue;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Services;

/// <summary>
/// The published <see cref="ITaskItems"/> port, wired to the feature slices
/// behind it. Deliberately nothing but mapping: every rule lives in a handler or
/// in the aggregate, so there is no third place to look.
/// </summary>
internal sealed class TaskItems(
    IQueryHandler<ListTasksQuery, IReadOnlyList<TaskItemDto>> list,
    ICommandHandler<SaveTaskFromTextCommand, Result<SavedTaskDto>> save,
    ICommandHandler<LinkTaskToIssueCommand, Result<TaskItemDto>> link,
    ICommandHandler<UnlinkTaskFromIssueCommand, Result<TaskItemDto>> unlink,
    ICommandHandler<SetDevbookReferencesCommand, Result<TaskItemDto>> setDevbookReferences,
    ICommandHandler<SetPlannedHoursCommand, Result<TaskItemDto>> setPlannedHours,
    ICommandHandler<DeleteTaskCommand> delete,
    ICommandHandler<ReorderTasksCommand> reorder,
    ICommandHandler<RecordTaskUsageCommand> recordUsage,
    ICommandHandler<ImportPlanCommand, Result<ImportPlanResultDto>> importPlan,
    ICommandHandler<ReconcileRepositoryIdsCommand, Result<int>> reconcileRepositoryIds,
    ICommandHandler<RenameRepositoryCommand, Result<int>> renameRepository,
    ICommandHandler<RefileRepositoryTasksCommand, Result<int>> refileRepositoryTasks,
    IQueryHandler<GetTaskQuery, TaskItemDto?> get,
    IQueryHandler<CountTasksQuery, int> count,
    ICommandHandler<CreateTaskIfAbsentCommand, Result<bool>> createIfAbsent,
    ICommandHandler<SetTaskDueCommand, Result<TaskItemDto>> setDue,
    IQueryHandler<GetTaskVersionQuery, DateTimeOffset?> getVersion) : ITaskItems
{
    public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
        list.Handle(new ListTasksQuery(), cancellationToken);

    public Task<TaskItemDto?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        get.Handle(new GetTaskQuery(id), cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        count.Handle(new CountTasksQuery(), cancellationToken);

    public Task<DateTimeOffset?> GetUpdatedAtAsync(Guid id, CancellationToken cancellationToken = default) =>
        getVersion.Handle(new GetTaskVersionQuery(id), cancellationToken);

    // The versioned writes hand the version to the handler, which checks it against
    // the entry it loads and saves conditionally on that same load - one atomic step,
    // where the port's defaults compare first and write after.
    public Task<Result<SavedTaskDto>> SaveFromTextAsync(
        Guid id,
        string rawText,
        int order,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default) =>
        RefuseStale(() => save.Handle(
            new SaveTaskFromTextCommand(id, rawText, order, ExpectedUpdatedAt: expectedUpdatedAt),
            cancellationToken));

    public Task<Result<TaskItemDto>> LinkToIssueAsync(
        Guid id,
        string repoId,
        string externalId,
        string targetType,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default) =>
        RefuseStale(() => link.Handle(
            new LinkTaskToIssueCommand(id, repoId, externalId, targetType, expectedUpdatedAt),
            cancellationToken));

    public Task<Result<TaskItemDto>> SetDevbookReferencesAsync(
        Guid id,
        IReadOnlyList<string> references,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default) =>
        RefuseStale(() => setDevbookReferences.Handle(
            new SetDevbookReferencesCommand(id, references, expectedUpdatedAt),
            cancellationToken));

    public Task<Result<SavedTaskDto>> SaveFromTextAsync(
        Guid? id,
        string rawText,
        int order,
        string? sourceInboxId = null,
        CancellationToken cancellationToken = default) =>
        RefuseStale(() => save.Handle(new SaveTaskFromTextCommand(id, rawText, order, sourceInboxId), cancellationToken));

    public Task<Result<bool>> CreateFromTextIfAbsentAsync(
        Guid id,
        string rawText,
        int order,
        CancellationToken cancellationToken = default) =>
        RefuseStale(() => createIfAbsent.Handle(new CreateTaskIfAbsentCommand(id, rawText, order), cancellationToken));

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        delete.Handle(new DeleteTaskCommand(id), cancellationToken);

    public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) =>
        reorder.Handle(new ReorderTasksCommand(idsInOrder), cancellationToken);

    public Task<Result<TaskItemDto>> LinkToIssueAsync(
        Guid id,
        string repoId,
        string externalId,
        string targetType,
        CancellationToken cancellationToken = default) =>
        RefuseStale(() => link.Handle(new LinkTaskToIssueCommand(id, repoId, externalId, targetType), cancellationToken));

    public Task<Result<TaskItemDto>> UnlinkFromIssueAsync(
        Guid id,
        string repoId,
        string externalId,
        string targetType,
        CancellationToken cancellationToken = default) =>
        RefuseStale(() => unlink.Handle(new UnlinkTaskFromIssueCommand(id, repoId, externalId, targetType), cancellationToken));

    public Task<Result<TaskItemDto>> SetDevbookReferencesAsync(
        Guid id,
        IReadOnlyList<string> references,
        CancellationToken cancellationToken = default) =>
        RefuseStale(() => setDevbookReferences.Handle(new SetDevbookReferencesCommand(id, references), cancellationToken));

    public Task<Result<TaskItemDto>> SetPlannedHoursAsync(
        Guid id,
        IReadOnlyList<PlannedHoursDto> blocks,
        CancellationToken cancellationToken = default) =>
        RefuseStale(() => setPlannedHours.Handle(new SetPlannedHoursCommand(id, blocks), cancellationToken));

    public Task<Result<TaskItemDto>> SetDueAsync(
        Guid id,
        DateOnly? dueOn,
        CancellationToken cancellationToken = default) =>
        RefuseStale(() => setDue.Handle(new SetTaskDueCommand(id, dueOn), cancellationToken));

    /// <summary>Records a usage event. A usage event is no edit and restamps
    /// nothing, so a refusal only means somebody saved the entry in between: the
    /// handler reads it again and records the event on that version, once. A
    /// caller recording usage after a write that landed — a comment — must not be
    /// told the write failed.</summary>
    public async Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default)
    {
        try
        {
            await recordUsage.Handle(new RecordTaskUsageCommand(id, action), cancellationToken).ConfigureAwait(false);
        }
        catch (StaleTaskWriteException)
        {
            await recordUsage.Handle(new RecordTaskUsageCommand(id, action), cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) =>
        RefuseStale(() => reconcileRepositoryIds.Handle(new ReconcileRepositoryIdsCommand(), cancellationToken));

    public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) =>
        RefuseStale(() => renameRepository.Handle(new RenameRepositoryCommand(oldId, newId), cancellationToken));

    public Task<Result<int>> RefileRepositoryTasksAsync(string repositoryId, string? moveToId, bool drop, CancellationToken cancellationToken = default) =>
        RefuseStale(() => refileRepositoryTasks.Handle(new RefileRepositoryTasksCommand(repositoryId, moveToId, drop), cancellationToken));

    public Task<Result<ImportPlanResultDto>> ImportPlanAsync(
        string rawText,
        string? defaultRepo = null,
        IReadOnlyDictionary<string, string>? repoMatches = null,
        string? sourceInboxId = null,
        IReadOnlyDictionary<string, string>? sourceInboxIds = null,
        bool layOutOnRoadmap = false,
        CancellationToken cancellationToken = default) =>
        RefuseStale(() => importPlan.Handle(new ImportPlanCommand(rawText, defaultRepo, repoMatches, sourceInboxId, layOutOnRoadmap, sourceInboxIds), cancellationToken));

    /// <summary>The refusal every write answering a <see cref="Result"/> gives
    /// when the store holds a later version of an entry than the one the write was
    /// made on: <see cref="StaleTaskWrites.StaleWrite"/>.</summary>
    public static Error StaleWrite => StaleTaskWrites.StaleWrite;

    /// <summary>
    /// Runs one write and answers a refused save as
    /// <see cref="StaleWrite"/>. The repository refuses by throwing, so a writer that
    /// does not expect it fails loudly; this port's callers — the editor, the MCP
    /// tools — each act on the refusal, so for them it is an ordinary result.
    /// </summary>
    private static async Task<Result<T>> RefuseStale<T>(Func<Task<Result<T>>> write)
    {
        try
        {
            return await write().ConfigureAwait(false);
        }
        catch (StaleTaskWriteException)
        {
            return StaleWrite;
        }
    }
}
