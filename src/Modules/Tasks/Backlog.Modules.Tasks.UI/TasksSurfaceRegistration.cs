using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// Puts the Tasks module's three views in the shell as view-group
/// <see cref="ShellSurface"/>s (local ADR 0042 §1): Tasks, Board and Calendar, the
/// one task list read three ways. On the precedent of <c>AddDevbookSurface</c>.
/// </summary>
/// <remarks>
/// Always offered: Tasks is always on. Safe to call twice — a second call adds
/// nothing — so a host composing the views beside the shared composition does not
/// trip the catalog's duplicate-id refusal. Adds the window's
/// <see cref="TasksViewWiring"/> too, one per window as the view state is.
/// </remarks>
public static class TasksSurfaceRegistration
{
    /// <summary>The views' places in the header, lowest first: Tasks, then Board, then
    /// Calendar; the Roadmap registers after them and Goals last.</summary>
    public const int TasksOrder = 10;
    public const int BoardOrder = 20;
    public const int CalendarOrder = 30;

    public static IServiceCollection AddTasksSurfaces(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShellSurface)
                && descriptor.ImplementationInstance is ShellSurface { Id: TasksSurfaceQuery.SurfaceId }))
        {
            return services;
        }

        services.AddSingleton(new ShellSurface(
            TasksSurfaceQuery.SurfaceId,
            "Tasks",
            ShellSurfaceGroup.View,
            TasksOrder,
            typeof(TasksSurface),
            QueryKeys: TasksSurfaceQuery.TasksKeys));
        services.AddSingleton(new ShellSurface(
            TasksSurfaceQuery.BoardId,
            "Board",
            ShellSurfaceGroup.View,
            BoardOrder,
            typeof(BoardSurface),
            QueryKeys: TasksSurfaceQuery.BoardKeys));
        services.AddSingleton(new ShellSurface(
            TasksSurfaceQuery.CalendarId,
            "Calendar",
            ShellSurfaceGroup.View,
            CalendarOrder,
            typeof(CalendarSurface)));

        services.TryAddScoped<TasksViewWiring>();

        return services;
    }
}
