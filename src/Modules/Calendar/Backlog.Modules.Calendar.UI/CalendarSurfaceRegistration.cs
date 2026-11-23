using global::Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Calendar.UI;

/// <summary>
/// Puts the Calendar in the shell as a view-group <see cref="ShellSurface"/> (local ADR
/// 0043 §1, local ADR 0055): the view that draws its own grid from the layers it reads
/// (local ADR 0056). Always offered, as Tasks is. The shell gives it a header option of
/// its own after the Tasks | Board | Roadmap switch. Safe to call twice.
/// </summary>
public static class CalendarSurfaceRegistration
{
    /// <summary>The view's id: what a route, a tool, a link and the shell's navigation
    /// file name — <c>lastView: calendar</c>, the name it always had.</summary>
    public const string SurfaceId = "calendar";

    /// <summary>The view's place among the views: after the Board (20), before the
    /// Roadmap. The shell draws its option outside the view switch, right after it.</summary>
    public const int Order = 30;

    public static IServiceCollection AddCalendarSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShellSurface)
                && descriptor.ImplementationInstance is ShellSurface { Id: SurfaceId }))
        {
            return services;
        }

        services.AddSingleton(new ShellSurface(
            SurfaceId,
            "Calendar",
            ShellSurfaceGroup.View,
            Order,
            typeof(CalendarSurface)));

        return services;
    }
}
