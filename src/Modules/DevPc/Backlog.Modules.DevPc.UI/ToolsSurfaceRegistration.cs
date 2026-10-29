using Backlog.Modules.DevPc.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.DevPc.UI;

/// <summary>
/// Puts Dev PC Management's Tools in the shell as a takeover-group
/// <see cref="ShellSurface"/> (local ADR 0040 §1), offered behind
/// <see cref="DevPcFeatures.SystemTools"/>, the flag its header option has always
/// gated on. On the precedent of <c>AddDevbookSurface</c>; safe to call twice.
/// </summary>
public static class ToolsSurfaceRegistration
{
    /// <summary>The Tools surface's id: what a route, a tool and a link name.</summary>
    public const string SurfaceId = "tools";

    /// <summary>Tools' place among the takeovers: last, after the Dashboard and the
    /// notification centre it sits beside in the header.</summary>
    public const int Order = 70;

    public static IServiceCollection AddToolsSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShellSurface)
                && descriptor.ImplementationInstance is ShellSurface { Id: SurfaceId }))
        {
            return services;
        }

        services.AddSingleton(new ShellSurface(
            SurfaceId,
            "Tools",
            ShellSurfaceGroup.Takeover,
            Order,
            typeof(ToolsSurface),
            DevPcFeatures.SystemTools));

        return services;
    }
}
