using Backlog.Modules.Progress.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Progress.UI.WeeklyReview;

/// <summary>
/// Puts the weekly review in the shell as a takeover-group <see cref="ShellSurface"/>
/// (local ADR 0043 §1), offered behind <see cref="ProgressFeatures.WeeklyReview"/>. Its
/// header option sits beside the Inbox's; like every takeover but the Inbox it closes on
/// Escape, since it keeps no key of its own. The host registers the window's
/// <see cref="IWeeklyReviewSource"/> beside it. On the precedent of
/// <c>AddInboxSurface</c>; safe to call twice.
/// </summary>
public static class WeeklyReviewSurfaceRegistration
{
    /// <summary>The weekly review's id: what a route, a tool and a link name.</summary>
    public const string SurfaceId = "weekly-review";

    /// <summary>Its place among the takeovers: after the Inbox (10), before the Devbook
    /// (20), as its option follows the Inbox's in the header.</summary>
    public const int Order = 15;

    public static IServiceCollection AddWeeklyReviewSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShellSurface)
                && descriptor.ImplementationInstance is ShellSurface { Id: SurfaceId }))
        {
            return services;
        }

        services.AddSingleton(new ShellSurface(
            SurfaceId,
            "Weekly review",
            ShellSurfaceGroup.Takeover,
            Order,
            typeof(WeeklyReviewSurface),
            ProgressFeatures.WeeklyReview));

        return services;
    }
}
