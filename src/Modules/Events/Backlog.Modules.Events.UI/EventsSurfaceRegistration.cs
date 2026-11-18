using global::Backlog.Modules.Events.Abstractions;
using global::Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Events.UI;

/// <summary>
/// Puts the Events pane in the shell as a takeover-group <see cref="ShellSurface"/>
/// (local ADR 0043 §1), offered behind <see cref="EventsFeatures.Events"/> (local ADR 0050 §6),
/// so with the area off nothing of it shows. It accepts <see cref="EventKey"/>, the event to
/// open on (§6). The pane reads <see cref="IEventsPane"/>, which <c>AddEventsModule()</c>
/// registers. Safe to call twice.
/// </summary>
public static class EventsSurfaceRegistration
{
    /// <summary>The pane's id: what a route, a tool and a link name.</summary>
    public const string SurfaceId = "events";

    /// <summary>The query key naming the event the pane opens on.</summary>
    public const string EventKey = "event";

    /// <summary>The pane's place among the takeovers: after the Devbook (20), before
    /// Sessions (30), where the header draws it.</summary>
    public const int Order = 25;

    public static IServiceCollection AddEventsSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShellSurface)
                && descriptor.ImplementationInstance is ShellSurface { Id: SurfaceId }))
        {
            return services;
        }

        services.AddSingleton(new ShellSurface(
            SurfaceId,
            "Events",
            ShellSurfaceGroup.Takeover,
            Order,
            typeof(EventsSurface),
            EventsFeatures.Events,
            [EventKey]));

        return services;
    }
}
