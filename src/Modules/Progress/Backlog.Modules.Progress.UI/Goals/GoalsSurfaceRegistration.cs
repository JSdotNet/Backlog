using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Progress.UI.Goals;

/// <summary>
/// Puts the Goals view in the shell as a view-group <see cref="ShellSurface"/> (local
/// ADR 0043 §1), always offered (local ADR 0035 §10). The host registers the
/// window's <see cref="IGoalsViewSource"/> beside it. On the precedent of
/// <c>AddDevbookSurface</c>; safe to call twice.
/// </summary>
public static class GoalsSurfaceRegistration
{
    /// <summary>Goals' place among the views: last. The header draws its option first
    /// in the work in progress group rather than in the view switch.</summary>
    public const int Order = 50;

    public static IServiceCollection AddGoalsSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShellSurface)
                && descriptor.ImplementationInstance is ShellSurface { Id: GoalsSurfaceQuery.SurfaceId }))
        {
            return services;
        }

        services.AddSingleton(new ShellSurface(
            GoalsSurfaceQuery.SurfaceId,
            "Goals",
            ShellSurfaceGroup.View,
            Order,
            typeof(GoalsSurface)));

        return services;
    }
}
