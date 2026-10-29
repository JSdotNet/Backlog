using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Desktop.UI.Devbook.Extensions;

/// <summary>
/// Puts the Devbook in the shell as a <see cref="ShellSurface"/> (local ADR 0042 §1).
/// </summary>
/// <remarks>
/// <para>
/// On the precedent of <c>AddInboxSettings</c>: whether a host's shell carries the
/// Devbook is the host's decision, and the shell knows the surface only as the
/// <see cref="ShellSurface"/> this adds — it never names
/// <see cref="DevbookSurface"/>. Offered behind
/// <see cref="DevbookFeatures.RepositoryDevbook"/>, the flag the Devbook option has
/// always gated on, so the catalog hides it wherever the flag is off.
/// </para>
/// <para>
/// Safe to call twice: a second call adds nothing, so a host that composes the
/// Devbook itself beside the shared composition does not trip the catalog's
/// duplicate-id refusal.
/// </para>
/// </remarks>
public static class DevbookSurfaceRegistration
{
    /// <summary>The Devbook's place among the takeovers. The Inbox, which leads the
    /// header, will register below it; the rest of the takeovers above.</summary>
    public const int Order = 20;

    public static IServiceCollection AddDevbookSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShellSurface)
                && descriptor.ImplementationInstance is ShellSurface { Id: DevbookSurface.SurfaceId }))
        {
            return services;
        }

        services.AddSingleton(new ShellSurface(
            DevbookSurface.SurfaceId,
            "Devbook",
            ShellSurfaceGroup.Takeover,
            Order,
            typeof(DevbookSurface),
            DevbookFeatures.RepositoryDevbook,
            DevbookSurfaceQuery.Keys));

        // Per window, as the window's view state is: which press each window's
        // Devbook already followed.
        services.TryAddScoped<DevbookSurfaceRequests>();

        return services;
    }
}
