using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// Puts the Connectors page on the settings screen, on the precedent of
/// <c>AddInboxSettings</c>: whether a host's settings screen carries it is the
/// host's decision, and the shell knows the page only as the
/// <see cref="SettingsSection"/> this adds.
/// </summary>
public static class TaskConnectorSettingsRegistration
{
    /// <summary>The Connectors page's place among the Product areas pages: after
    /// the Inbox's.</summary>
    public const int Order = 40;

    public static IServiceCollection AddTaskConnectorSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new SettingsSection(
            "connectors",
            "Connectors",
            Order,
            typeof(TaskConnectorSettings),
            Group: SettingsGroup.ProductAreas,
            CardTitles: TaskConnectorSettings.CardTitles,
            BadgeSource: typeof(ConnectedTaskSourcesBadgeSource)));

        // The value the menu draws beside the page's name, a singleton as
        // ISettingsBadgeSource asks: the shell holds it while the screen is open.
        services.TryAddSingleton<ConnectedTaskSourcesBadgeSource>();

        return services;
    }
}
