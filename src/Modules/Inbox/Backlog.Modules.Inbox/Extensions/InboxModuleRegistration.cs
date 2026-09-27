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
using Backlog.Modules.Inbox.Features.MoveListToGroup;
using Backlog.Modules.Inbox.Features.MoveToList;
using Backlog.Modules.Inbox.Features.OpenAttachment;
using Backlog.Modules.Inbox.Features.ReadAttachment;
using Backlog.Modules.Inbox.Features.ReceiveCapture;
using Backlog.Modules.Inbox.Features.RenameGroup;
using Backlog.Modules.Inbox.Features.RenameList;
using Backlog.Modules.Inbox.Features.RetryAttachment;
using Backlog.Modules.Inbox.Features.ResurfaceDueItems;
using Backlog.Modules.Inbox.Features.ResurfaceItem;
using Backlog.Modules.Inbox.Features.RouteToBacklog;
using Backlog.Modules.Inbox.Features.SetTags;
using Backlog.Modules.Inbox.Features.Suggest;
using Backlog.Modules.Inbox.Features.UngroupLists;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Modules.Inbox.Extensions;

/// <summary>
/// The module's composition root. A host calls this once and gets every use
/// case the Inbox context offers; it never registers a handler itself, and
/// never sees the aggregate.
/// <para>
/// Four things are deliberately not registered here. The two repository ports
/// (<see cref="IInboxItemRepository"/>, <see cref="IInboxOrganizerRepository"/>)
/// are internal ports whose adapter is the host's decision, as Tasks' is. The
/// two outward ports (<see cref="IInboxBacklogTarget"/>,
/// <see cref="IInboxPlanDrafter"/>) are answered by adapters that see other
/// contexts, which only a host may compose — and the drafter may be left out
/// altogether, in which case the handlers answer <c>inbox.plan.not_configured</c>.
/// </para>
/// </summary>
public static class InboxModuleRegistration
{
    public static IServiceCollection AddInboxModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The handlers stamp with an injected clock so a test can place a
        // decision in time. TryAdd, so a host that already chose one keeps it.
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IQueryHandler<GetInboxQuery, InboxSnapshotDto>, GetInboxQueryHandler>();
        services.AddScoped<ICommandHandler<CaptureItemCommand, Result<InboxItemDto>>, CaptureItemCommandHandler>();
        services.AddScoped<ICommandHandler<SetTagsCommand, Result>, SetTagsCommandHandler>();
        services.AddScoped<ICommandHandler<AssignRepositoriesCommand, Result>, AssignRepositoriesCommandHandler>();
        services.AddScoped<ICommandHandler<RenameRepositoryCommand, Result<int>>, RenameRepositoryCommandHandler>();
        services.AddScoped<ICommandHandler<MoveToListCommand, Result>, MoveToListCommandHandler>();
        services.AddScoped<ICommandHandler<ArchiveItemCommand, Result>, ArchiveItemCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteItemCommand, Result>, DeleteItemCommandHandler>();
        services.AddScoped<ICommandHandler<DeferItemCommand, Result>, DeferItemCommandHandler>();
        services.AddScoped<ICommandHandler<ResurfaceItemCommand, Result>, ResurfaceItemCommandHandler>();
        services.AddScoped<ICommandHandler<ResurfaceDueItemsCommand, Result<int>>, ResurfaceDueItemsCommandHandler>();
        services.AddScoped<ICommandHandler<RouteToBacklogCommand, Result<InboxRoutedDto>>, RouteToBacklogCommandHandler>();
        services.AddScoped<ICommandHandler<CreatePlanCommand, Result<InboxRoutedDto>>, CreatePlanCommandHandler>();
        services.AddScoped<ICommandHandler<CreateListCommand, Result<InboxListDto>>, CreateListCommandHandler>();
        services.AddScoped<ICommandHandler<RenameListCommand, Result>, RenameListCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteListCommand, Result>, DeleteListCommandHandler>();
        services.AddScoped<ICommandHandler<MoveListToGroupCommand, Result>, MoveListToGroupCommandHandler>();
        services.AddScoped<ICommandHandler<CreateGroupCommand, Result<InboxGroupDto>>, CreateGroupCommandHandler>();
        services.AddScoped<ICommandHandler<RenameGroupCommand, Result>, RenameGroupCommandHandler>();
        services.AddScoped<ICommandHandler<UngroupListsCommand, Result>, UngroupListsCommandHandler>();
        services.AddScoped<ICommandHandler<EnsureDefaultOrganizerCommand>, EnsureDefaultOrganizerCommandHandler>();

        // The file acts on the pane. The two attachment ports they take are
        // optional constructor parameters the host may leave out — see
        // IInboxAttachmentSource — so a head without them still builds, and the
        // handlers answer inbox.attachment.unavailable.
        services.AddScoped<ICommandHandler<RetryAttachmentCommand, Result>, RetryAttachmentCommandHandler>();
        services.AddScoped<IQueryHandler<ReadAttachmentQuery, Result<byte[]>>, ReadAttachmentQueryHandler>();
        services.AddScoped<ICommandHandler<OpenAttachmentCommand, Result>, OpenAttachmentCommandHandler>();

        // Classification's suggestions. The tag source and the routing rules
        // the query reads are optional constructor parameters, like the plan
        // drafter: a host without them is offered what the item's own text
        // supports.
        services.AddScoped<IQueryHandler<SuggestQuery, Result<IReadOnlyList<InboxSuggestionDto>>>, SuggestQueryHandler>();
        services.AddScoped<ICommandHandler<DismissSuggestionCommand, Result>, DismissSuggestionCommandHandler>();

        services.AddScoped<IInboxItems, InboxItems>();

        // The two sync-facing ports, and the one slice behind them, are
        // transient where everything above is scoped. The task sync loop is a
        // singleton that resolves its session from the root provider, outside
        // any scope, and a scoped registration there is an InvalidOperationException
        // the loop reads as "this host does not replicate" — captures would stop
        // arriving with nothing on screen to say so. Transient is safe because all
        // three capture only the item repository and the clock, which a host
        // registers as singletons the way it does Tasks' repository — and the
        // two optional attachment ports the intake fetches files through, which a
        // host registers as singletons or transients for the same reason.
        services.AddTransient<ICommandHandler<ReceiveCaptureCommand, Result<InboxIntakeOutcome>>, ReceiveCaptureCommandHandler>();
        services.AddTransient<IInboxIntake, InboxIntake>();
        services.AddTransient<IInboxCaptureOutbox, InboxCaptureOutbox>();

        return services;
    }
}
