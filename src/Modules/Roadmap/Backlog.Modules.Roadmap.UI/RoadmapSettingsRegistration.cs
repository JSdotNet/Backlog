using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// Puts Work's capacity page on the settings screen: the working week.
/// </summary>
/// <remarks>
/// <para>
/// The week is capacity, Work's with the pace and the day overrides (local ADR 0073
/// §1); it moved here from the Dashboard's settings, which read it and never write it.
/// The shell knows the page only as the <see cref="SettingsSection"/> this adds.
/// Called from <see cref="RoadmapSurfaceRegistration.AddRoadmapSurface"/>, so every
/// host that composes the roadmap carries the page without a line of its own.
/// </para>
/// <para>
/// Offered with no switch: the roadmap counts the week (ADR 0019) and the dashboard
/// outlines it. The section keeps its id and its place among the Product areas pages,
/// straight after the Insights page, so a remembered settings page still opens it.
/// </para>
/// </remarks>
public static class RoadmapSettingsRegistration
{
    /// <summary>The working week's page: straight after the Insights page.</summary>
    public const int WorkingWeekOrder = 20;

    public static IServiceCollection AddRoadmapSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(SettingsSection)
                && descriptor.ImplementationInstance is SettingsSection { Id: "working-week" }))
        {
            return services;
        }

        services.AddSingleton(new SettingsSection(
            "working-week",
            "Working week",
            WorkingWeekOrder,
            typeof(WorkingWeekSettings),
            Group: SettingsGroup.ProductAreas,
            CardTitles: [WorkingWeekSettings.CardTitle]));

        return services;
    }
}
