using Backlog.Modules.Events.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.FileSystem.Events;

/// <summary>
/// What other contexts read of the Events context, answered by adapters that may see both
/// sides (local ADR 0050 §7): the Tasks Calendar's events layer
/// (<see cref="ICalendarEvents"/>) over Events' read contract.
/// <para>
/// Registered here — in one place both hosts call through the shared composition — so the
/// lifetimes cannot drift between the desktop app and the web harness.
/// </para>
/// </summary>
public static class EventsCrossContextAdapterRegistration
{
    /// <summary>
    /// Registers the Events cross-context adapters. The read contract they ask is
    /// <c>AddEventsModule</c>'s, resolved when the adapter is first asked for, so the call
    /// order does not matter.
    /// </summary>
    public static IServiceCollection AddEventsCrossContextAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The Calendar's events, read through Events' published contract and never
        // written. A singleton over singletons — the reader, the feature switches and the
        // shell's per-device file — for the reason ICalendarCapacity is one: it captures no
        // scoped service. A host that composed no such file holds the box in memory.
        services.AddSingleton<ICalendarEvents>(sp =>
            new EventsCalendarEvents(
                sp.GetRequiredService<IKnowledgeEventReader>(),
                sp.GetService<IAppFeatureSettings>(),
                sp.GetService<ShellNavigationStore>()));

        return services;
    }
}
