using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// Puts the Tasks module's two views in the shell as view-group
/// <see cref="ShellSurface"/>s (local ADR 0043 §1): Tasks and Board, the one task list
/// read two ways. On the precedent of <c>AddDevbookSurface</c>. The Calendar is its own
/// module's surface and draws its own grid (local ADR 0056); it reads the tasks through
/// its own port, which an adapter answers over Tasks' published <c>ITaskItems</c>, so
/// Tasks registers nothing for it.
/// </summary>
/// <remarks>
/// Always offered: Tasks is always on. Safe to call twice — a second call adds
/// nothing — so a host composing the views beside the shared composition does not
/// trip the catalog's duplicate-id refusal. Adds the window's
/// <see cref="TasksViewWiring"/> too, one per window as the view state is.
/// </remarks>
public static class TasksSurfaceRegistration
{
    /// <summary>The views' places in the header, lowest first: Tasks, then Board; the
    /// Calendar registers after them, then the Roadmap, and Goals last.</summary>
    public const int TasksOrder = 10;
    public const int BoardOrder = 20;

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

        services.TryAddScoped<TasksViewWiring>();

        return services;
    }
}
