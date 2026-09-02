using Backlog.Modules.Tasks.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// Wires the adapters that answer Tasks' workspace-facing ports.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <c>AddTasksModule()</c> on purpose, the same split the
/// Dashboard makes with <c>AddDashboardAdapters()</c>. That call brings the use
/// cases; this one decides which providers are behind their ports, which is the
/// host's choice — and a test replaces this call rather than having to unpick it.
/// </para>
/// <para>
/// Both hosts must call this after registering <c>GitHubSettingsStore</c>; the
/// adapter only holds that store and does not construct it.
/// </para>
/// <para>
/// A singleton, unlike the scoped handlers the module registers. The adapter
/// holds no per-import state — it reads the settings store on every access, so a
/// repository configured mid-session is resolvable the next time a plan is
/// imported.
/// </para>
/// </remarks>
public static class TasksAdapterRegistration
{
    public static IServiceCollection AddTasksAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IRepositoryDirectory, SettingsRepositoryDirectory>();

        return services;
    }
}
