using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.PullRequests;

/// <summary>
/// Puts the open pull requests in the shell as a takeover-group
/// <see cref="ShellSurface"/> (local ADR 0043 §1), offered behind
/// <see cref="RepositoryManagementFeatures.PullRequests"/> — the flag its header option
/// has always gated on, its key unchanged since it moved off <c>SessionFeatures</c>.
/// It accepts <see cref="PullRequestsSurface.PullRequestKey"/>, the pull request to open
/// on (§6).
/// <para>
/// The list is Repository Management's screen (local ADR 0042), beside the sessions
/// that opened them. It still asks the host's GitHub integration for the registry's
/// aliases, colours and label filters, which a module UI project may not reference, so
/// it is drawn and registered here, beside the rest of the host's pull request code —
/// the one surface not registered from its module's <c>.UI</c> project. On the
/// precedent of <c>AddDevbookSurface</c>; safe to call twice.
/// </para>
/// </summary>
public static class PullRequestsSurfaceRegistration
{
    /// <summary>The list's place among the takeovers: right after the session list it
    /// sits beside in the header.</summary>
    public const int Order = 40;

    public static IServiceCollection AddPullRequestsSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShellSurface)
                && descriptor.ImplementationInstance is ShellSurface { Id: PullRequestsSurface.SurfaceId }))
        {
            return services;
        }

        services.AddSingleton(new ShellSurface(
            PullRequestsSurface.SurfaceId,
            "Pull requests",
            ShellSurfaceGroup.Takeover,
            Order,
            typeof(PullRequestsSurface),
            RepositoryManagementFeatures.PullRequests,
            [PullRequestsSurface.PullRequestKey]));

        return services;
    }
}
