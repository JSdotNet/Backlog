using Backlog.Modules.Events.Abstractions.Services;
using Backlog.Modules.Events.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Modules.Events.Extensions;

/// <summary>
/// The module's composition root (guideline ADR 0005, local ADR 0050 §7). A host calls
/// <see cref="AddEventsModule"/> once.
/// <para>
/// The module publishes one port, the events read contract <see cref="IKnowledgeEventReader"/>:
/// the decided events and their key dates, which the Tasks Calendar reads through a port of its
/// own. The repositories, <see cref="IKnowledgeEventRepository"/> and
/// <see cref="IEventSeriesRepository"/>, are the host's to register — today the
/// <c>events_*</c> tables in <c>backlog.db</c> — as <c>INotificationRepository</c> is.
/// </para>
/// </summary>
public static class EventsModuleRegistration
{
    public static IServiceCollection AddEventsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        // A singleton over the host's repository, which is one too: the reader holds nothing
        // but that port. TryAdd, so a second call adds nothing.
        services.TryAddSingleton<IKnowledgeEventReader, KnowledgeEventReader>();

        return services;
    }
}
