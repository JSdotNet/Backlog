using Backlog.Modules.Dashboard.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Dashboard.UI.Extensions;

/// <summary>
/// Puts the Dashboard in the shell as a takeover-group <see cref="ShellSurface"/>
/// (local ADR 0043 §1), offered behind <see cref="DashboardFeatures.Dashboard"/>, the
/// flag its header option has always gated on. On the precedent of
/// <c>AddDevbookSurface</c>; safe to call twice.
/// </summary>
public static class DashboardSurfaceRegistration
{
    /// <summary>The Dashboard surface's id: what a route, a tool and a link name.</summary>
    public const string SurfaceId = "dashboard";

    /// <summary>The Dashboard's place among the takeovers: after the Sessions context's
    /// two lists, first of the loose group it leads in the header.</summary>
    public const int Order = 50;

    public static IServiceCollection AddDashboardSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShellSurface)
                && descriptor.ImplementationInstance is ShellSurface { Id: SurfaceId }))
        {
            return services;
        }

        services.AddSingleton(new ShellSurface(
            SurfaceId,
            "Dashboard",
            ShellSurfaceGroup.Takeover,
            Order,
            typeof(DashboardSurface),
            DashboardFeatures.Dashboard));

        return services;
    }
}
