using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Ai;
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

/// <summary>
/// Wires Tasks' answer to <see cref="IAiContentSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// Its own call rather than a line in <see cref="TasksAdapterRegistration"/>,
/// because the source reads <see cref="TasksDesktopState"/> and the adapters do
/// not: a host — or the scope-validation test — that composes the module and its
/// adapters without a pane's state must still build, and a registration that
/// needed the state would fail it.
/// </para>
/// <para>
/// Scoped, for a reason the hosts disagree on: the desktop registers the state
/// once per window and the web harness once per circuit. A singleton over the
/// harness's state is a captive dependency validate-on-build refuses, and a
/// scoped object over the desktop's singleton state is a cheap wrapper resolved
/// once per window — so scoped is the one lifetime that is correct in both, and
/// the source holds nothing of its own to make it matter. A host registers the
/// state before calling this.
/// </para>
/// </remarks>
public static class TasksAiContentRegistration
{
    public static IServiceCollection AddTasksAiContentSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAiContentSource, TasksAiContentSource>();

        return services;
    }
}
