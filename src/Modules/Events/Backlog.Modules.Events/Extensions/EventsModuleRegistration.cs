using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.Services;
using Backlog.Modules.Events.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Modules.Events.Extensions;

/// <summary>
/// The module's composition root (guideline ADR 0005, local ADR 0050 §7). A host calls
/// <see cref="AddEventsModule"/> once.
/// <para>
/// The module publishes two ports: <see cref="IKnowledgeEvents"/>, the contract the Inbox's To
/// Event route reaches Events through, and the events read contract
/// <see cref="IKnowledgeEventReader"/>: the decided events and their key dates, which the Tasks
/// Calendar reads through a port of its own. The repositories,
/// <see cref="IKnowledgeEventRepository"/> and <see cref="IEventSeriesRepository"/>, are the
/// host's to register — today the <c>events_*</c> tables in <c>backlog.db</c> — as
/// <c>INotificationRepository</c> is. Both are singletons there, so the contracts over them are
/// too.
/// </para>
/// <para>
/// It also publishes <see cref="IEventsPane"/>, the Events pane's port. That one is Scoped,
/// one per window, because it holds Tasks' scoped port through <see cref="IEventTaskTarget"/>.
/// </para>
/// </summary>
public static class EventsModuleRegistration
{
    public static IServiceCollection AddEventsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IKnowledgeEvents, KnowledgeEvents>();
        services.TryAddScoped<IEventsPane, EventsPane>();

        // A singleton over the host's repository, which is one too: the reader holds nothing
        // but that port. TryAdd, so a second call adds nothing.
        services.TryAddSingleton<IKnowledgeEventReader, KnowledgeEventReader>();

        // The replication port the sync client's knowledge event exchange reads and writes
        // through (local ADR 0050 §2), over the host's replica stores when it registered them.
        services.TryAddSingleton<IKnowledgeEventReplication, KnowledgeEventReplication>();

        // Where the sync client applies the phone's attachment requests (local ADR 0050 §3):
        // a Talk note linked to an event, over the host's event repository.
        services.TryAddSingleton<IEventAttachmentRequests, EventAttachmentRequests>();

        return services;
    }
}
