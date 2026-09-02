using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Features.DeleteTask;
using Backlog.Modules.Tasks.Features.ImportPlan;
using Backlog.Modules.Tasks.Features.LinkTaskToIssue;
using Backlog.Modules.Tasks.Features.ListTasks;
using Backlog.Modules.Tasks.Features.RecordTaskUsage;
using Backlog.Modules.Tasks.Features.ReorderTasks;
using Backlog.Modules.Tasks.Features.SaveTaskFromText;
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
    ICommandHandler<DeleteTaskCommand> delete,
    ICommandHandler<ReorderTasksCommand> reorder,
    ICommandHandler<RecordTaskUsageCommand> recordUsage,
    ICommandHandler<ImportPlanCommand, Result<ImportPlanResultDto>> importPlan) : ITaskItems
{
    public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
        list.Handle(new ListTasksQuery(), cancellationToken);

    public Task<Result<SavedTaskDto>> SaveFromTextAsync(
        Guid? id,
        string rawText,
        int order,
        CancellationToken cancellationToken = default) =>
        save.Handle(new SaveTaskFromTextCommand(id, rawText, order), cancellationToken);

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
        link.Handle(new LinkTaskToIssueCommand(id, repoId, externalId, targetType), cancellationToken);

    public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) =>
        recordUsage.Handle(new RecordTaskUsageCommand(id, action), cancellationToken);

    public Task<Result<ImportPlanResultDto>> ImportPlanAsync(
        string rawText,
        string? defaultRepo = null,
        IReadOnlyDictionary<string, string>? repoMatches = null,
        CancellationToken cancellationToken = default) =>
        importPlan.Handle(new ImportPlanCommand(rawText, defaultRepo, repoMatches), cancellationToken);
}
