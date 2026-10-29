using Backlog.Desktop.UI.Devbook.Extensions;
using Backlog.Desktop.UI.InProgress;
using Backlog.Desktop.UI.Shell;
using Backlog.Desktop.UI.Tasks;
using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Progress.UI.Goals;
using Backlog.Modules.Roadmap.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What <c>AddDesktopComposition</c> registers for the shell's surfaces (local ADR
/// 0040 §1, §3), for the tests that render <c>Home</c> with a container of their own:
/// the five views' surfaces and the Devbook's, what the views read beside the list,
/// the catalog, the window's navigator, the task titles a Brain note draws, and the
/// window scope a Devbook opened with no scope reads.
/// </summary>
internal static class ShellSurfaceTestServices
{
    public static IServiceCollection AddShellSurfaces(this IServiceCollection services)
    {
        services.AddTasksSurfaces();
        services.AddRoadmapSurface();
        services.AddGoalsSurface();
        services.AddDevbookSurface();
        services.AddSingleton<ITasksViewPreferences>(sp => new ShellTasksViewPreferences(sp.GetRequiredService<ShellNavigationStore>()));
        services.AddScoped(sp => new ShellGoalsViewSource(sp));
        services.AddScoped<IGoalsViewSource>(sp => sp.GetRequiredService<ShellGoalsViewSource>());
        services.AddSingleton<ISurfaceCatalog, SurfaceCatalog>();
        services.AddScoped<ISurfaceNavigator, SurfaceNavigator>();
        services.AddScoped<IBrainTaskTitles>(sp => new ShellBrainTaskTitles(sp.GetRequiredService<TasksDesktopState>()));
        services.AddScoped<IDevbookWindowScope>(sp => new ShellDevbookWindowScope(sp.GetRequiredService<TasksDesktopState>()));
        return services;
    }
}
