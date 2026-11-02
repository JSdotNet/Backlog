using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.Devbook.Extensions;

/// <summary>
/// Puts the Lobes atlas in the shell as a takeover-group <see cref="ShellSurface"/>
/// (local ADR 0043 §1; <c>.devbook/domain/devbook/features.md#lobes-atlas</c>),
/// offered behind <see cref="DevbookFeatures.LobesAtlas"/>, the flag its header option
/// has always gated on. The atlas maps what the Devbook reads, so the shell offers it
/// only while the Devbook's own flag is on as well (<c>DevbookScope.LobesAtlasOffered</c>).
/// On the precedent of <c>AddDevbookSurface</c>; safe to call twice.
/// </summary>
public static class AtlasSurfaceRegistration
{
    /// <summary>The atlas's id: what a route, a tool and a link name. The retired
    /// <c>WorkspaceSurface</c> enum stored it as <c>Atlas</c>.</summary>
    public const string SurfaceId = "atlas";

    /// <summary>The Lobes scope the atlas opens on — <c>brain</c>,
    /// <c>repo:&lt;alias&gt;</c> or <c>ghost:&lt;alias&gt;</c>; without it, All. Not
    /// <c>scope</c>: that key names a repository or the Brain to every surface that
    /// reads one (local ADR 0043 §6), and a window of its own scopes its chips by it.</summary>
    public const string ScopeKey = "layout";

    /// <summary>The atlas's place among the takeovers: after Tools, the last of the
    /// loose group it sits in in the header.</summary>
    public const int Order = 80;

    public static IServiceCollection AddAtlasSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShellSurface)
                && descriptor.ImplementationInstance is ShellSurface { Id: SurfaceId }))
        {
            return services;
        }

        services.AddSingleton(new ShellSurface(
            SurfaceId,
            "Atlas",
            ShellSurfaceGroup.Takeover,
            Order,
            typeof(AtlasSurface),
            DevbookFeatures.LobesAtlas,
            [ScopeKey]));

        return services;
    }
}
