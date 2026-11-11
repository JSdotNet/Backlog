using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Infrastructure.FileSystem.Events;

/// <summary>
/// The cross-context joins Events takes part in, answered by adapters that may see both sides
/// (local ADR 0050 §3, §7): its prep tasks are filed and its task chips read through
/// <see cref="IEventTaskTarget"/> over Tasks' <see cref="ITaskItems"/>, its note links read
/// through <see cref="IEventBrainNotes"/>, and the Tasks Calendar's events layer
/// (<see cref="ICalendarEvents"/>) reads Events' read contract.
/// <para>
/// The task target captures a service the Tasks module registers as <c>Scoped</c>, so it is
/// <c>Scoped</c> too; a singleton over it is a captive dependency a validating provider refuses.
/// Registered here — in one place both hosts call through the shared composition — so the
/// lifetimes cannot drift between the desktop app and the web harness.
/// </para>
/// </summary>
public static class EventsCrossContextAdapterRegistration
{
    /// <summary>
    /// Registers Events' cross-context adapters. Call after <c>AddTasksModule</c>, which supplies
    /// the scoped port and the change signal the task target captures. The read contract the
    /// Calendar's adapter asks is <c>AddEventsModule</c>'s, resolved when the adapter is first
    /// asked for.
    /// </summary>
    public static IServiceCollection AddEventsCrossContextAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IEventTaskTarget, EventsTaskTarget>();

        // Every note missing, and said why, until a host composes the Brain —
        // Backlog.Infrastructure.Brain's registration then replaces this with the reader
        // over the Brain's folder and database.
        services.TryAddSingleton<IEventBrainNotes, NoEventBrainNotes>();

        // The Calendar's events, read through Events' published contract and never
        // written. A singleton over singletons — the reader, the feature switches and the
        // shell's per-device file — for the reason ICalendarCapacity is one: it captures no
        // scoped service. A host that composed no such file holds the box in memory.
        services.TryAddSingleton<ICalendarEvents>(sp =>
            new EventsCalendarEvents(
                sp.GetRequiredService<IKnowledgeEventReader>(),
                sp.GetService<IAppFeatureSettings>(),
                sp.GetService<ShellNavigationStore>()));

        return services;
    }
}
