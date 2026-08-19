using Backlog.Modules.Backlog.Abstractions.DataTransferObjects;
using Backlog.Modules.Backlog.Abstractions.Services;
using Backlog.Modules.Backlog.Features.DeleteEntry;
using Backlog.Modules.Backlog.Features.LinkEntryToIssue;
using Backlog.Modules.Backlog.Features.ListEntries;
using Backlog.Modules.Backlog.Features.RecordEntryUsage;
using Backlog.Modules.Backlog.Features.ReorderEntries;
using Backlog.Modules.Backlog.Features.SaveEntryFromText;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Backlog.Services;

/// <summary>
/// The published <see cref="IBacklogEntries"/> port, wired to the feature slices
/// behind it. Deliberately nothing but mapping: every rule lives in a handler or
/// in the aggregate, so there is no third place to look.
/// </summary>
internal sealed class BacklogEntries(
    IQueryHandler<ListEntriesQuery, IReadOnlyList<BacklogEntryDto>> list,
    ICommandHandler<SaveEntryFromTextCommand, Result<SavedEntryDto>> save,
    ICommandHandler<LinkEntryToIssueCommand, Result<BacklogEntryDto>> link,
    ICommandHandler<DeleteEntryCommand> delete,
    ICommandHandler<ReorderEntriesCommand> reorder,
    ICommandHandler<RecordEntryUsageCommand> recordUsage) : IBacklogEntries
{
    public Task<IReadOnlyList<BacklogEntryDto>> ListAsync(CancellationToken cancellationToken = default) =>
        list.Handle(new ListEntriesQuery(), cancellationToken);

    public Task<Result<SavedEntryDto>> SaveFromTextAsync(
        Guid? id,
        string rawText,
        int order,
        CancellationToken cancellationToken = default) =>
        save.Handle(new SaveEntryFromTextCommand(id, rawText, order), cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        delete.Handle(new DeleteEntryCommand(id), cancellationToken);

    public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) =>
        reorder.Handle(new ReorderEntriesCommand(idsInOrder), cancellationToken);

    public Task<Result<BacklogEntryDto>> LinkToIssueAsync(
        Guid id,
        string repoId,
        string externalId,
        string targetType,
        CancellationToken cancellationToken = default) =>
        link.Handle(new LinkEntryToIssueCommand(id, repoId, externalId, targetType), cancellationToken);

    public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) =>
        recordUsage.Handle(new RecordEntryUsageCommand(id, action), cancellationToken);
}
