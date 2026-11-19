using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// Puts the Roadmap in the shell as a view-group <see cref="ShellSurface"/> (local ADR
/// 0043 §1), always offered. On the precedent of <c>AddDevbookSurface</c>; safe to
/// call twice.
/// </summary>
public static class RoadmapSurfaceRegistration
{
    /// <summary>The Roadmap's place among the views: after Tasks, Board and Calendar.</summary>
    public const int Order = 40;

    public static IServiceCollection AddRoadmapSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShellSurface)
                && descriptor.ImplementationInstance is ShellSurface { Id: RoadmapSurfaceQuery.SurfaceId }))
        {
            return services;
        }

        services.AddSingleton(new ShellSurface(
            RoadmapSurfaceQuery.SurfaceId,
            "Roadmap",
            ShellSurfaceGroup.View,
            Order,
            typeof(RoadmapSurface),
            null,
            RoadmapSurfaceQuery.Keys));

        return services;
    }
}
