using Backlog.Modules.Progress.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Progress.UI;

/// <summary>
/// Puts Settings › Sprints on the settings screen.
/// </summary>
/// <remarks>
/// On the precedent of <c>InboxSettingsRegistration</c>: the shell knows the page only
/// as the <see cref="SettingsSection"/> this adds, and offers it behind
/// <see cref="ProgressFeatures.Progress"/>, the switch every Progress screen is drawn
/// under — so with Progress off there is no Sprints tab on the strip.
/// </remarks>
public static class ProgressSettingsRegistration
{
    /// <summary>The Sprints page's place among the sections modules register: after the
    /// Dashboard's and the working week's.</summary>
    public const int Order = 220;

    public static IServiceCollection AddProgressSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new SettingsSection("sprints", "Sprints", Order, typeof(SprintSettings), ProgressFeatures.Progress));

        return services;
    }
}
