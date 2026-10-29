using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Sessions.UI;

/// <summary>
/// Puts the Sessions context's session list in the shell as a takeover-group
/// <see cref="ShellSurface"/> (local ADR 0041 §1), offered behind
/// <see cref="SessionFeatures.Sessions"/>, the flag its header option has always gated
/// on. It accepts <see cref="SessionKey"/>, the session to open on (§6). On the
/// precedent of <c>AddDevbookSurface</c>; safe to call twice.
/// <para>
/// The context's second list, the open pull requests, is drawn by the host, which reads
/// GitHub for it; its id is named here because it is this context's.
/// </para>
/// </summary>
public static class SessionsSurfaceRegistration
{
    /// <summary>The session list's id: what a route, a tool and a link name.</summary>
    public const string SurfaceId = "sessions";

    /// <summary>The pull requests list's id, the context's second surface.</summary>
    public const string PullRequestsSurfaceId = "pull-requests";

    /// <summary>The query key naming the session the list opens on.</summary>
    public const string SessionKey = "session";

    /// <summary>The session list's place among the takeovers: after the Inbox and the
    /// Devbook, leading the work in progress it sits in in the header.</summary>
    public const int Order = 30;

    public static IServiceCollection AddSessionsSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShellSurface)
                && descriptor.ImplementationInstance is ShellSurface { Id: SurfaceId }))
        {
            return services;
        }

        services.AddSingleton(new ShellSurface(
            SurfaceId,
            "Sessions",
            ShellSurfaceGroup.Takeover,
            Order,
            typeof(SessionsSurface),
            SessionFeatures.Sessions,
            [SessionKey]));

        return services;
    }
}
