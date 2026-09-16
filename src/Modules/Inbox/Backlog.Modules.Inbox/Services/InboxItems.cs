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
using Backlog.Modules.Inbox.Features.DeleteList;
using Backlog.Modules.Inbox.Features.EnsureDefaultOrganizer;
using Backlog.Modules.Inbox.Features.GetInbox;
using Backlog.Modules.Inbox.Features.MoveListToGroup;
using Backlog.Modules.Inbox.Features.MoveToList;
using Backlog.Modules.Inbox.Features.RenameGroup;
using Backlog.Modules.Inbox.Features.RenameList;
using Backlog.Modules.Inbox.Features.RouteToBacklog;
using Backlog.Modules.Inbox.Features.SetTags;
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
    ICommandHandler<RouteToBacklogCommand, Result<InboxRoutedDto>> routeToBacklog,
    ICommandHandler<CreatePlanCommand, Result<InboxRoutedDto>> createPlan,
    ICommandHandler<CreateListCommand, Result<InboxListDto>> createList,
    ICommandHandler<RenameListCommand, Result> renameList,
    ICommandHandler<DeleteListCommand, Result> deleteList,
    ICommandHandler<MoveListToGroupCommand, Result> moveListToGroup,
    ICommandHandler<CreateGroupCommand, Result<InboxGroupDto>> createGroup,
    ICommandHandler<RenameGroupCommand, Result> renameGroup,
    ICommandHandler<UngroupListsCommand, Result> ungroup,
    ICommandHandler<EnsureDefaultOrganizerCommand> ensureDefaultOrganizer,
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

    public Task<Result<InboxRoutedDto>> RouteToBacklogAsync(Guid id, CancellationToken cancellationToken = default) =>
        routeToBacklog.Handle(new RouteToBacklogCommand(id), cancellationToken);

    public Task<Result<InboxRoutedDto>> CreatePlanAsync(Guid id, CancellationToken cancellationToken = default) =>
        createPlan.Handle(new CreatePlanCommand(id), cancellationToken);

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
}
