using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// Puts the Tasks page on the settings screen, on the precedent of
/// <c>AddInboxSettings</c>: whether a host's settings screen carries it is the
/// host's decision, and the shell knows the page only as the
/// <see cref="SettingsSection"/> this adds.
/// </summary>
/// <remarks>
/// Offered with no feature switch: Tasks is always on, and so is My Day. The page
/// reads and writes <c>IMyDayPreferences</c>, which a host that adds the page
/// registers (local ADR 0032).
/// </remarks>
public static class TasksSettingsRegistration
{
    /// <summary>The Tasks page's place among the Product areas pages: after the
    /// Inbox's (30) and before the Connectors (40).</summary>
    public const int Order = 35;

    public static IServiceCollection AddTasksSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new SettingsSection(
            "tasks",
            "Tasks",
            Order,
            typeof(TasksSettings),
            Group: SettingsGroup.ProductAreas,
            CardTitles: TasksSettings.CardTitles));

        return services;
    }
}
