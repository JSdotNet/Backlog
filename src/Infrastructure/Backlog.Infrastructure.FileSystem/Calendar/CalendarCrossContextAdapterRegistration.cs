using Backlog.Modules.Calendar.Abstractions.Services;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Infrastructure.FileSystem.Calendar;

/// <summary>
/// What the Calendar reads from the contexts it draws layers of (local ADR 0055 §2),
/// answered by adapters that may see both sides: the roadmap's plans
/// (<see cref="ICalendarPlans"/>) and the hours worked a day
/// (<see cref="ICalendarCapacity"/>), each over Roadmap's own abstractions. The Events
/// layer still reaches the Calendar through Tasks' <c>ICalendarEvents</c> until it moves.
/// <para>
/// Registered here — in one place both hosts and the scope-validation guard call — so
/// the lifetimes cannot drift between the desktop app and the web harness.
/// </para>
/// </summary>
public static class CalendarCrossContextAdapterRegistration
{
    /// <summary>
    /// Registers the Calendar's cross-context adapters. Call after <c>AddRoadmapModule</c>
    /// and <c>AddRoadmapCrossContextAdapters</c>, whose ports these resolve when first
    /// asked. Safe to call twice.
    /// </summary>
    public static IServiceCollection AddCalendarCrossContextAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The Plans layer's port, read and started through Roadmap's own ports — Scoped,
        // because every one of them is. The work and pace signals are what the roadmap
        // band redraws on, so the Calendar hears them too.
        services.TryAddScoped<ICalendarPlans>(sp =>
            new RoadmapCalendarPlans(
                sp.GetRequiredService<IRoadmapPlanning>(),
                sp.GetRequiredService<IRoadmapItemRollup>(),
                sp.GetRequiredService<IPlanningVelocity>(),
                sp.GetRequiredService<IImportedPlanSource>(),
                sp.GetService<IAppFeatureSettings>(),
                sp.GetService<IRoadmapWorkChanges>(),
                sp.GetService<IPlanningPace>()));

        // The hours a Calendar day is set against: the roadmap's working week and days
        // off, read-only. A singleton over the singleton settings port. Planned hours go
        // nowhere the other way.
        services.TryAddSingleton<ICalendarCapacity>(sp =>
            new RoadmapCalendarCapacity(sp.GetRequiredService<IPlanningVelocitySettings>()));

        return services;
    }
}
