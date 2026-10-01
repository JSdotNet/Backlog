using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Copilot;

/// <summary>
/// The Copilot CLI launcher every "open in Copilot CLI" offer starts through,
/// registered in one place both desktop heads reach through their shared
/// composition.
/// </summary>
public static class CopilotRegistration
{
    /// <summary>
    /// Registers <paramref name="launcher"/> as the one <see cref="ICopilotCliLauncher"/>,
    /// a singleton. Which launcher is the host's to say: a head on the person's
    /// machine starts the CLI with <see cref="ProcessCopilotCliLauncher"/>, and a
    /// host that may not start a process passes <see cref="UnavailableCopilotCliLauncher"/>,
    /// so pressing the offer says so rather than doing nothing.
    /// </summary>
    public static IServiceCollection AddCopilot(this IServiceCollection services, Func<IServiceProvider, ICopilotCliLauncher> launcher)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(launcher);

        services.AddSingleton(launcher);

        return services;
    }
}
