using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Sessions.UI.Extensions;

/// <summary>
/// Puts the Sessions context's page on the settings screen: where Claude Code's
/// telemetry is received, and what to paste into Claude Code to send it.
/// </summary>
/// <remarks>
/// Offered behind <see cref="SessionFeatures.Sessions"/>, the area the received cost is
/// shown in, on the precedent of <c>AddInboxSettings</c>.
/// </remarks>
public static class SessionsSettingsRegistration
{
    /// <summary>The page's place among the sections modules register.</summary>
    public const int Order = 150;

    public static IServiceCollection AddSessionsSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new SettingsSection(
            "claude-code",
            "Claude Code",
            Order,
            typeof(ClaudeCodeTelemetrySettings),
            SessionFeatures.Sessions));

        return services;
    }
}
