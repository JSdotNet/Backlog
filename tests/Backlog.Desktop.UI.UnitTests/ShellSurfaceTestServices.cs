using Backlog.Desktop.UI.Devbook.Extensions;
using Backlog.Desktop.UI.Shell;
using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Devbook.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What <c>AddDesktopComposition</c> registers for the shell's surfaces (local ADR
/// 0040 §1, §3), for the tests that render <c>Home</c> with a container of their own:
/// the Devbook's surface, the catalog, the window's navigator, and the task titles a
/// Brain note draws.
/// </summary>
internal static class ShellSurfaceTestServices
{
    public static IServiceCollection AddShellSurfaces(this IServiceCollection services)
    {
        services.AddDevbookSurface();
        services.AddSingleton<ISurfaceCatalog, SurfaceCatalog>();
        services.AddScoped<ISurfaceNavigator, SurfaceNavigator>();
        services.AddScoped<IBrainTaskTitles>(sp => new ShellBrainTaskTitles(sp.GetRequiredService<TasksDesktopState>()));
        return services;
    }
}
