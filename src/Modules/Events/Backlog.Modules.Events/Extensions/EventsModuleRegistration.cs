using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Modules.Events.Extensions;

/// <summary>
/// The module's composition root (guideline ADR 0005, local ADR 0050 §7). A host calls
/// <see cref="AddEventsModule"/> once.
/// <para>
/// The module publishes no port yet: no pane or other context reads it. The repositories,
/// <see cref="IKnowledgeEventRepository"/> and <see cref="IEventSeriesRepository"/>, are the
/// host's to register — today the <c>events_*</c> tables in <c>backlog.db</c> — as
/// <c>INotificationRepository</c> is.
/// </para>
/// </summary>
public static class EventsModuleRegistration
{
    public static IServiceCollection AddEventsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
