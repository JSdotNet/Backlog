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
    /// Registers the module. It owns no data, so it registers no store; its one service is
    /// the Plans layer's read policy, Scoped — one per window, as the
    /// <see cref="ICalendarPlans"/> port it reads is. The port is optional: a host that
    /// composed none gets a layer that offers no plans. Safe to call twice.
    /// </summary>
    public static IServiceCollection AddCalendarModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ICalendarPlansLayer>(sp =>
            new CalendarPlansLayer(sp.GetService<ICalendarPlans>(), sp.GetService<IAppFeatureSettings>()));

        return services;
    }
}
