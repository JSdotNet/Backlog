using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Infrastructure.FileSystem.Events;

/// <summary>
/// The cross-context joins Events takes part in (local ADR 0050 §3): its prep tasks are filed
/// and its task chips read through <see cref="IEventTaskTarget"/> over Tasks'
/// <see cref="ITaskItems"/>, and its note links read through <see cref="IEventBrainNotes"/>.
/// <para>
/// The task target captures a service the Tasks module registers as <c>Scoped</c>, so it is
/// <c>Scoped</c> too; a singleton over it is a captive dependency a validating provider refuses.
/// </para>
/// </summary>
public static class EventsCrossContextAdapterRegistration
{
    /// <summary>
    /// Registers Events' cross-context adapters. Call after <c>AddTasksModule</c>, which supplies
    /// the scoped port and the change signal the task target captures.
    /// </summary>
    public static IServiceCollection AddEventsCrossContextAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IEventTaskTarget, EventsTaskTarget>();

        // Every note missing, and said why, until a host composes the Brain —
        // Backlog.Infrastructure.Brain's registration then replaces this with the reader
        // over the Brain's folder and database.
        services.TryAddSingleton<IEventBrainNotes, NoEventBrainNotes>();

        return services;
    }
}
