using Backlog.Modules.Inbox.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Inbox.UI;

/// <summary>
/// Puts the Inbox in the shell as a takeover-group <see cref="ShellSurface"/> (local
/// ADR 0043 §1), offered behind <see cref="InboxFeatures.Pane"/>, the flag its header
/// option has always gated on. Escape does not close it: the Inbox's own keys use
/// Escape to leave triage. On the precedent of <c>AddDevbookSurface</c>; safe to call
/// twice.
/// </summary>
public static class InboxSurfaceRegistration
{
    /// <summary>The Inbox surface's id: what a route, a tool and a link name.</summary>
    public const string SurfaceId = "inbox";

    /// <summary>The Inbox's place among the takeovers: first, as it leads the header.</summary>
    public const int Order = 10;

    public static IServiceCollection AddInboxSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShellSurface)
                && descriptor.ImplementationInstance is ShellSurface { Id: SurfaceId }))
        {
            return services;
        }

        services.AddSingleton(new ShellSurface(
            SurfaceId,
            "Inbox",
            ShellSurfaceGroup.Takeover,
            Order,
            typeof(InboxSurface),
            InboxFeatures.Pane,
            EscapeCloses: false));

        return services;
    }
}
