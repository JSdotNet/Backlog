using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Modules.Sessions.UI.Extensions;

/// <summary>
/// Puts the Sessions context's pages on the settings screen: where Claude Code's
/// telemetry is received, and what to paste into Claude Code to send it; and the
/// model prices that estimate a run it never reported on.
/// </summary>
/// <remarks>
/// Offered behind <see cref="SessionFeatures.Sessions"/>, the area the received cost is
/// shown in, on the precedent of <c>AddInboxSettings</c>.
/// </remarks>
public static class SessionsSettingsRegistration
{
    /// <summary>The page's place among the AI pages, after the shell's AI providers.</summary>
    public const int Order = 150;

    public static IServiceCollection AddSessionsSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new SettingsSection(
            "claude-code",
            "Claude Code",
            Order,
            typeof(ClaudeCodeTelemetrySettings),
            SessionFeatures.Sessions,
            SettingsGroup.Ai,
            CardTitles: [ClaudeCodeTelemetrySettings.CardTitle]));

        // The rates that price a run Claude Code reported no cost for, beside the page
        // that says how to have it report one.
        services.AddSingleton(new SettingsSection(
            "model-prices",
            "Model prices",
            Order + 1,
            typeof(ModelPriceSettings),
            SessionFeatures.Sessions,
            SettingsGroup.Ai,
            CardTitles: [ModelPriceSettings.CardTitle],
            BadgeSource: typeof(ModelPriceBadgeSource)));

        // The value the menu draws beside the page's name, a singleton as
        // ISettingsBadgeSource asks: the shell holds it while the screen is open.
        services.TryAddSingleton<ModelPriceBadgeSource>();

        return services;
    }
}
