using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Insights.UI.Extensions;

/// <summary>
/// Puts the Dashboard in the shell as a takeover-group <see cref="ShellSurface"/>
/// (local ADR 0043 §1), always offered. On the precedent of
/// <c>AddDevbookSurface</c>; safe to call twice.
/// </summary>
public static class InsightsSurfaceRegistration
{
    /// <summary>The Dashboard surface's id: what a route, a tool and a link name.</summary>
    public const string SurfaceId = "insights";

    /// <summary>The Dashboard's place among the takeovers: after the Sessions context's
    /// two lists, first of the loose group it leads in the header.</summary>
    public const int Order = 50;

    public static IServiceCollection AddInsightsSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShellSurface)
                && descriptor.ImplementationInstance is ShellSurface { Id: SurfaceId }))
        {
            return services;
        }

        services.AddSingleton(new ShellSurface(
            SurfaceId,
            "Insights",
            ShellSurfaceGroup.Takeover,
            Order,
            typeof(InsightsSurface),
            null,
            QueryKeys: [InsightsSurface.TabKey]));

        return services;
    }
}
