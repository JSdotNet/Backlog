using Backlog.Modules.Roadmap.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// Puts the Roadmap in the shell as a view-group <see cref="ShellSurface"/> (local ADR
/// 0041 §1), offered behind <see cref="RoadmapFeatures.Roadmap"/>, the flag its view
/// option has always gated on. On the precedent of <c>AddDevbookSurface</c>; safe to
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
            RoadmapFeatures.Roadmap,
            RoadmapSurfaceQuery.Keys));

        return services;
    }
}
