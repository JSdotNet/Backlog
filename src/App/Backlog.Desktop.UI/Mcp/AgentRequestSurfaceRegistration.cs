using Backlog.Desktop.UI.Shell;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// The request window as a shell surface (local ADR 0052 §5, ADR 0043 §1): a takeover the
/// header does not offer, because it is opened on a request — by the agent through the broker
/// or <c>request_access</c>, or by the request's notification — and never chosen from a menu.
/// </summary>
public static class AgentRequestSurfaceRegistration
{
    /// <summary>Its place among the takeovers; it is in no header group, so the number only
    /// orders the catalog.</summary>
    public const int Order = 90;

    public static IServiceCollection AddAgentRequestSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShellSurface)
                && descriptor.ImplementationInstance is ShellSurface { Id: AgentRequestSurface.SurfaceId }))
        {
            return services;
        }

        services.AddSingleton(new ShellSurface(
            AgentRequestSurface.SurfaceId,
            "Agent request",
            ShellSurfaceGroup.Takeover,
            Order,
            typeof(AgentRequestSurface),
            FeatureKey: null,
            [AgentRequestSurface.RequestKey]));

        return services;
    }
}

/// <summary>
/// <see cref="IAgentRequestWindows"/> over the head's windows: the request window on a request,
/// or the one already open on it brought forward.
/// </summary>
/// <param name="windows">The head's windows.</param>
public sealed class ShellAgentRequestWindows(IShellWindows windows) : IAgentRequestWindows
{
    /// <inheritdoc />
    public Task<bool> ShowAsync(string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        return windows.FocusOrOpenAsync(
            AgentRequestSurface.SurfaceId,
            new Dictionary<string, string>(StringComparer.Ordinal) { [AgentRequestSurface.RequestKey] = requestId });
    }
}
