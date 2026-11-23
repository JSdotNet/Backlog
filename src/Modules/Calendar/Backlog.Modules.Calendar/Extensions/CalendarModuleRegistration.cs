using Backlog.Modules.Calendar.Abstractions.Services;
using Backlog.Modules.Calendar.Services;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Modules.Calendar.Extensions;

/// <summary>
/// The Calendar module's composition root (guideline ADR 0005, local ADR 0055 §6).
/// </summary>
public static class CalendarModuleRegistration
{
    /// <summary>
    /// Registers the module. It owns no data, so it registers no store; its services are
    /// the layers' read policies, each Scoped — one per window: the Plans layer, as the
    /// <see cref="ICalendarPlans"/> port it reads is, the Events layer over
    /// <see cref="ICalendarEvents"/>, and the Tasks layer over <see cref="ICalendarTasks"/>
    /// (local ADR 0056 §2). Each port is optional: a host that composed none gets
    /// a layer that offers nothing. Safe to call twice.
    /// </summary>
    public static IServiceCollection AddCalendarModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ICalendarPlansLayer>(sp =>
            new CalendarPlansLayer(sp.GetService<ICalendarPlans>()));

        services.TryAddScoped<ICalendarEventsLayer>(sp =>
            new CalendarEventsLayer(sp.GetService<ICalendarEvents>(), sp.GetService<IAppFeatureSettings>()));

        services.TryAddScoped<ICalendarTasksLayer>(sp => new CalendarTasksLayer(sp.GetService<ICalendarTasks>()));

        return services;
    }
}
