using Backlog.SharedKernel;
using Backlog.SharedKernel.Changes;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.Changes;

/// <summary>
/// Puts the Changes view in the shell as a view-group <see cref="ShellSurface"/> (local
/// ADR 0043 §1): the change journal read back (local ADR 0061), always offered. The
/// journal is shared infrastructure, the way Sync is, and no module owns it, so the
/// view is drawn and registered here, in the shell's own project. It accepts
/// <see cref="ChangesSurfaceQuery.SessionKey"/>, the agent session to narrow to.
/// Safe to call twice.
/// </summary>
public static class ChangesSurfaceRegistration
{
    /// <summary>The view's place among the views: after the Roadmap (40) and Goals (50).
    /// The header draws its option last in the view switch.</summary>
    public const int Order = 60;

    public static IServiceCollection AddChangesSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShellSurface)
                && descriptor.ImplementationInstance is ShellSurface { Id: ChangesSurfaceQuery.SurfaceId }))
        {
            return services;
        }

        services.AddSingleton(new ShellSurface(
            ChangesSurfaceQuery.SurfaceId,
            "Changes",
            ShellSurfaceGroup.View,
            Order,
            typeof(ChangesSurface),
            QueryKeys: ChangesSurfaceQuery.Keys));

        return services;
    }
}
