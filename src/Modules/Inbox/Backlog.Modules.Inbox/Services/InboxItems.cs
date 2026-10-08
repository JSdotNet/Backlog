using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Features.ArchiveItem;
using Backlog.Modules.Inbox.Features.AssignRepositories;
using Backlog.Modules.Inbox.Features.RenameRepository;
using Backlog.Modules.Inbox.Features.CaptureItem;
using Backlog.Modules.Inbox.Features.CreateGroup;
using Backlog.Modules.Inbox.Features.CreateList;
using Backlog.Modules.Inbox.Features.CreatePlan;
using Backlog.Modules.Inbox.Features.DeferItem;
using Backlog.Modules.Inbox.Features.DeleteItem;
using Backlog.Modules.Inbox.Features.DeleteList;
using Backlog.Modules.Inbox.Features.DismissSuggestion;
using Backlog.Modules.Inbox.Features.EnsureDefaultOrganizer;
using Backlog.Modules.Inbox.Features.GetInbox;
using Backlog.Modules.Inbox.Features.LinkToTask;
using Backlog.Modules.Inbox.Features.MoveListToGroup;
using Backlog.Modules.Inbox.Features.MoveToList;
using Backlog.Modules.Inbox.Features.OpenAttachment;
using Backlog.Modules.Inbox.Features.InferBatchOrder;
using Backlog.Modules.Inbox.Features.ProposeBatch;
using Backlog.Modules.Inbox.Features.ReadAttachment;
using Backlog.Modules.Inbox.Features.Related;
using Backlog.Modules.Inbox.Features.RenameGroup;
using Backlog.Modules.Inbox.Features.RenameList;
using Backlog.Modules.Inbox.Features.RetryAttachment;
using Backlog.Modules.Inbox.Features.ResurfaceDueItems;
using Backlog.Modules.Inbox.Features.ResurfaceItem;
using Backlog.Modules.Inbox.Features.RouteBatchToBacklog;
using Backlog.Modules.Inbox.Features.RouteToBacklog;
using Backlog.Modules.Inbox.Features.SetTags;
using Backlog.Modules.Inbox.Features.Suggest;
using Backlog.Modules.Inbox.Features.UngroupLists;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Services;

/// <summary>
/// The published <see cref="IInboxItems"/> port, wired to the feature slices
/// behind it. Deliberately nothing but mapping, as <c>TaskItems</c> is: every
/// rule lives in a handler or in the aggregate, so there is no third place to
/// look. The one thing it answers itself is <see cref="PlanDrafterAvailability"/>,
/// which is a read of the drafter's own property and not a rule.
/// </summary>
internal sealed class InboxItems(
    IQueryHandler<GetInboxQuery, InboxSnapshotDto> snapshot,
    ICommandHandler<CaptureItemCommand, Result<InboxItemDto>> capture,
    ICommandHandler<SetTagsCommand, Result> setTags,
    ICommandHandler<AssignRepositoriesCommand, Result> assignRepositories,
    ICommandHandler<RenameRepositoryCommand, Result<int>> renameRepository,
    ICommandHandler<MoveToListCommand, Result> moveToList,
    ICommandHandler<ArchiveItemCommand, Result> archive,
    ICommandHandler<DeleteItemCommand, Result> delete,
    ICommandHandler<DeferItemCommand, Result> defer,
    ICommandHandler<ResurfaceItemCommand, Result> resurface,
    ICommandHandler<ResurfaceDueItemsCommand, Result<int>> resurfaceDue,
    ICommandHandler<RouteToBacklogCommand, Result<InboxRoutedDto>> routeToBacklog,
    ICommandHandler<CreatePlanCommand, Result<InboxRoutedDto>> createPlan,
    ICommandHandler<RouteBatchToBacklogCommand, Result<InboxBatchRoutedDto>> routeBatchToBacklog,
    IQueryHandler<ProposeBatchQuery, Result<InboxBatchProposalDto>> proposeBatch,
    IQueryHandler<InferBatchOrderQuery, Result<IReadOnlyList<ProposedDependency>>> inferBatchOrder,
    ICommandHandler<CreateListCommand, Result<InboxListDto>> createList,
    ICommandHandler<RenameListCommand, Result> renameList,
    ICommandHandler<DeleteListCommand, Result> deleteList,
    ICommandHandler<MoveListToGroupCommand, Result> moveListToGroup,
    ICommandHandler<CreateGroupCommand, Result<InboxGroupDto>> createGroup,
    ICommandHandler<RenameGroupCommand, Result> renameGroup,
    ICommandHandler<UngroupListsCommand, Result> ungroup,
    ICommandHandler<EnsureDefaultOrganizerCommand> ensureDefaultOrganizer,
    ICommandHandler<RetryAttachmentCommand, Result> retryAttachment,
    IQueryHandler<ReadAttachmentQuery, Result<byte[]>> readAttachment,
    ICommandHandler<OpenAttachmentCommand, Result> openAttachment,
    IQueryHandler<SuggestQuery, Result<IReadOnlyList<InboxSuggestionDto>>> suggest,
    IQueryHandler<SuggestManyQuery, Result<IReadOnlyDictionary<Guid, IReadOnlyList<InboxSuggestionDto>>>> suggestMany,
    ICommandHandler<DismissSuggestionCommand, Result> dismissSuggestion,
    IQueryHandler<RelatedQuery, Result<InboxRelationsDto>> related,
    ICommandHandler<LinkToTaskCommand, Result> linkToTask,
    IInboxPlanDrafter? drafter = null) : IInboxItems
{
    public Task<InboxSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        snapshot.Handle(new GetInboxQuery(), cancellationToken);

    public Task<Result<InboxItemDto>> CaptureAsync(
        string title,
        string? notes = null,
        string channel = InboxEnumMap.ManualChannel,
        CancellationToken cancellationToken = default) =>
        capture.Handle(new CaptureItemCommand(title, notes, channel), cancellationToken);

    public Task<Result> SetTagsAsync(Guid id, IReadOnlyList<string> tags, CancellationToken cancellationToken = default) =>
        setTags.Handle(new SetTagsCommand(id, tags), cancellationToken);

    public Task<Result> AssignRepositoriesAsync(Guid id, IReadOnlyList<string> repoIds, CancellationToken cancellationToken = default) =>
        assignRepositories.Handle(new AssignRepositoriesCommand(id, repoIds), cancellationToken);

    public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) =>
        renameRepository.Handle(new RenameRepositoryCommand(oldId, newId), cancellationToken);

    public Task<Result> MoveToListAsync(Guid id, Guid? listId, CancellationToken cancellationToken = default) =>
        moveToList.Handle(new MoveToListCommand(id, listId), cancellationToken);

    public Task<Result> ArchiveAsync(Guid id, CancellationToken cancellationToken = default) =>
        archive.Handle(new ArchiveItemCommand(id), cancellationToken);

    public Task<Result> ArchiveAsDuplicateAsync(Guid id, Guid duplicateOf, CancellationToken cancellationToken = default) =>
        archive.Handle(new ArchiveItemCommand(id, duplicateOf), cancellationToken);

    public Task<Result> LinkToTaskAsync(Guid id, Guid taskId, CancellationToken cancellationToken = default) =>
        linkToTask.Handle(new LinkToTaskCommand(id, taskId), cancellationToken);

    public Task<Result<InboxRelationsDto>> RelatedAsync(Guid id, CancellationToken cancellationToken = default) =>
        related.Handle(new RelatedQuery(id), cancellationToken);

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        delete.Handle(new DeleteItemCommand(id), cancellationToken);

    public Task<Result> DeferAsync(Guid id, DateOnly? until, CancellationToken cancellationToken = default) =>
        defer.Handle(new DeferItemCommand(id, until), cancellationToken);

    public Task<Result> ResurfaceAsync(Guid id, CancellationToken cancellationToken = default) =>
        resurface.Handle(new ResurfaceItemCommand(id), cancellationToken);

    public Task<Result<int>> ResurfaceDueAsync(CancellationToken cancellationToken = default) =>
        resurfaceDue.Handle(new ResurfaceDueItemsCommand(), cancellationToken);

    public Task<InboxBatchResultDto> SetTagsAsync(
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> tagsByItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tagsByItem);

        return EachAsync(
            [.. tagsByItem.Keys],
            id => setTags.Handle(new SetTagsCommand(id, tagsByItem[id]), cancellationToken));
    }

    public Task<InboxBatchResultDto> AssignRepositoriesAsync(
        IReadOnlyList<Guid> ids,
        IReadOnlyList<string> repoIds,
        CancellationToken cancellationToken = default) =>
        EachAsync(ids, id => assignRepositories.Handle(new AssignRepositoriesCommand(id, repoIds), cancellationToken));

    public Task<InboxBatchResultDto> MoveToListAsync(
        IReadOnlyList<Guid> ids,
        Guid? listId,
        CancellationToken cancellationToken = default) =>
        EachAsync(ids, id => moveToList.Handle(new MoveToListCommand(id, listId), cancellationToken));

    public Task<InboxBatchResultDto> ArchiveAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
        EachAsync(ids, id => archive.Handle(new ArchiveItemCommand(id), cancellationToken));

    public Task<InboxBatchResultDto> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
        EachAsync(ids, id => delete.Handle(new DeleteItemCommand(id), cancellationToken));

    public Task<Result<InboxRoutedDto>> RouteToBacklogAsync(Guid id, CancellationToken cancellationToken = default) =>
        routeToBacklog.Handle(new RouteToBacklogCommand(id), cancellationToken);

    public Task<Result<InboxBatchProposalDto>> ProposeBatchAsync(
        IReadOnlyList<Guid> ids,
        Guid? listId = null,
        CancellationToken cancellationToken = default) =>
        proposeBatch.Handle(new ProposeBatchQuery(ids, listId), cancellationToken);

    public Task<Result<IReadOnlyList<ProposedDependency>>> InferBatchOrderAsync(
        IReadOnlyList<Guid> ids,
        string planTag,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? repositories = null,
        CancellationToken cancellationToken = default) =>
        inferBatchOrder.Handle(new InferBatchOrderQuery(ids, planTag, repositories), cancellationToken);

    public Task<Result<InboxBatchRoutedDto>> RouteToBacklogAsync(
        IReadOnlyList<Guid> ids,
        Guid? listId = null,
        InboxBatchRouteChoicesDto? choices = null,
        CancellationToken cancellationToken = default) =>
        routeBatchToBacklog.Handle(new RouteBatchToBacklogCommand(ids, listId, choices), cancellationToken);

    public Task<Result<InboxRoutedDto>> CreatePlanAsync(Guid id, CancellationToken cancellationToken = default) =>
        createPlan.Handle(new CreatePlanCommand(id), cancellationToken);

    public Task<Result> RetryAttachmentAsync(Guid id, Guid attachmentId, CancellationToken cancellationToken = default) =>
        retryAttachment.Handle(new RetryAttachmentCommand(id, attachmentId), cancellationToken);

    public Task<Result<byte[]>> ReadAttachmentAsync(Guid id, Guid attachmentId, CancellationToken cancellationToken = default) =>
        readAttachment.Handle(new ReadAttachmentQuery(id, attachmentId), cancellationToken);

    public Task<Result> OpenAttachmentAsync(Guid id, Guid attachmentId, CancellationToken cancellationToken = default) =>
        openAttachment.Handle(new OpenAttachmentCommand(id, attachmentId), cancellationToken);

    public Task<Result<IReadOnlyList<InboxSuggestionDto>>> SuggestAsync(Guid id, CancellationToken cancellationToken = default) =>
        suggest.Handle(new SuggestQuery(id), cancellationToken);

    public Task<Result<IReadOnlyDictionary<Guid, IReadOnlyList<InboxSuggestionDto>>>> SuggestManyAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
        suggestMany.Handle(new SuggestManyQuery(ids), cancellationToken);

    public Task<Result> DismissSuggestionAsync(Guid id, string key, CancellationToken cancellationToken = default) =>
        dismissSuggestion.Handle(new DismissSuggestionCommand(id, key), cancellationToken);

    public (bool Available, string? Reason) PlanDrafterAvailability =>
        drafter is { IsAvailable: true }
            ? (true, null)
            : (false, InboxErrors.PlanNotConfigured(drafter?.UnavailableReason).Message);

    public Task<Result<InboxListDto>> CreateListAsync(string name, Guid? groupId = null, CancellationToken cancellationToken = default) =>
        createList.Handle(new CreateListCommand(name, groupId), cancellationToken);

    public Task<Result> RenameListAsync(Guid listId, string name, CancellationToken cancellationToken = default) =>
        renameList.Handle(new RenameListCommand(listId, name), cancellationToken);

    public Task<Result> DeleteListAsync(Guid listId, CancellationToken cancellationToken = default) =>
        deleteList.Handle(new DeleteListCommand(listId), cancellationToken);

    public Task<Result> MoveListToGroupAsync(Guid listId, Guid? groupId, CancellationToken cancellationToken = default) =>
        moveListToGroup.Handle(new MoveListToGroupCommand(listId, groupId), cancellationToken);

    public Task<Result<InboxGroupDto>> CreateGroupAsync(string name, CancellationToken cancellationToken = default) =>
        createGroup.Handle(new CreateGroupCommand(name), cancellationToken);

    public Task<Result> RenameGroupAsync(Guid groupId, string name, CancellationToken cancellationToken = default) =>
        renameGroup.Handle(new RenameGroupCommand(groupId, name), cancellationToken);

    public Task<Result> UngroupAsync(Guid groupId, CancellationToken cancellationToken = default) =>
        ungroup.Handle(new UngroupListsCommand(groupId), cancellationToken);

    public Task EnsureDefaultOrganizerAsync(CancellationToken cancellationToken = default) =>
        ensureDefaultOrganizer.Handle(new EnsureDefaultOrganizerCommand(), cancellationToken);

    /// <summary>One command per item, in order, and one after another rather
    /// than at once — the handlers share a store — with each answer sorted into
    /// changed or refused. Mapping still, not a rule: the command decides every
    /// item, and this only keeps count.</summary>
    private static async Task<InboxBatchResultDto> EachAsync(IReadOnlyList<Guid> ids, Func<Guid, Task<Result>> handle)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0) return InboxBatchResultDto.Nothing;

        var changed = new List<Guid>();
        var failed = new List<InboxBatchFailureDto>();

        foreach (var id in ids.Distinct())
        {
            var result = await handle(id).ConfigureAwait(false);

            if (result.IsSuccess) changed.Add(id);
            else failed.Add(new InboxBatchFailureDto(id, result.Error));
        }

        return new InboxBatchResultDto(changed, failed);
    }
}
