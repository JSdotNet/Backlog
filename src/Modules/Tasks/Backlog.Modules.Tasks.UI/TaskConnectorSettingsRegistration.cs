using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

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
            CardTitles: TaskConnectorSettings.CardTitles));

        return services;
    }
}
