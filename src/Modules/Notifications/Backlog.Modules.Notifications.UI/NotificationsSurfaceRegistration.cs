using global::Backlog.Modules.Notifications.Abstractions;
using global::Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Notifications.UI;

/// <summary>
/// Puts the notification centre in the shell as a takeover-group
/// <see cref="ShellSurface"/> (local ADR 0040 §1), offered behind
/// <see cref="NotificationsFeatures.Notifications"/>, the flag its header option has
/// always gated on. It accepts <see cref="NotificationKey"/>, the notification to open
/// on (§6). The centre reads <see cref="INotificationCentre"/>, which only an owner
/// instance registers, so a host calls this where it calls <c>AddNotificationsModule()</c>
/// as the owner. On the precedent of <c>AddDevbookSurface</c>; safe to call twice.
/// </summary>
public static class NotificationsSurfaceRegistration
{
    /// <summary>The centre's id: what a route, a tool and a link name.</summary>
    public const string SurfaceId = "notifications";

    /// <summary>The query key naming the notification the centre opens on.</summary>
    public const string NotificationKey = "notification";

    /// <summary>The centre's place among the takeovers: between the Dashboard and Tools,
    /// as in the header's loose group.</summary>
    public const int Order = 60;

    public static IServiceCollection AddNotificationsSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShellSurface)
                && descriptor.ImplementationInstance is ShellSurface { Id: SurfaceId }))
        {
            return services;
        }

        services.AddSingleton(new ShellSurface(
            SurfaceId,
            "Notifications",
            ShellSurfaceGroup.Takeover,
            Order,
            typeof(NotificationsSurface),
            NotificationsFeatures.Notifications,
            [NotificationKey]));

        return services;
    }
}
