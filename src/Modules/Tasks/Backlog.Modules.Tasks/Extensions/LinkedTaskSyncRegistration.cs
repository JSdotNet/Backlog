using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.Features.CompleteLinkedTask;
using Backlog.Modules.Tasks.Features.SyncLinkedTasks;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Modules.Tasks.Extensions;

/// <summary>
/// How a host brings in linked tasks (ADR 0020): the connectors it ships, the sync
/// that runs them, the timer, and the write-back that finishes their items at the
/// source.
/// <para>
/// Here rather than beside <see cref="ITaskConnector"/> in the abstractions,
/// because that project references nothing but the shared kernel and a container
/// is not part of the published language. A connector's project references this
/// one for the registration line, the way the infrastructure adapters already do.
/// </para>
/// <para>
/// Separate from <see cref="TasksModuleRegistration.AddTasksModule"/>, because the
/// sync needs an <see cref="IConnectedTargets"/> only a desktop head provides: the
/// MCP server, the tests and any head that composes the module without it would
/// otherwise hold a handler that cannot be constructed.
/// </para>
/// </summary>
public static class LinkedTaskSyncRegistration
{
    /// <summary>Registers a connector. Adding the same one twice registers it
    /// once.</summary>
    public static IServiceCollection AddTaskConnector<TConnector>(this IServiceCollection services)
        where TConnector : class, ITaskConnector
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITaskConnector, TConnector>());
        return services;
    }

    /// <summary>
    /// Registers the sync and its timer, and the write-back a save asks for when it
    /// finishes a linked task. The host registers the
    /// <see cref="IConnectedTargets"/> store, and resolves
    /// <see cref="LinkedTaskSyncWorker"/> once after building, or the timer never
    /// starts.
    /// </summary>
    public static IServiceCollection AddLinkedTaskSync(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ICommandHandler<SyncLinkedTasksCommand, Result<LinkedTaskSyncSummary>>, SyncLinkedTasksCommandHandler>();

        // One, because the schedule is what it holds: a second would be a second
        // timer running the same targets.
        services.TryAddSingleton<LinkedTaskSyncWorker>();

        // The same worker behind the port the settings screen's "Sync now" asks, so
        // a request and the timer share one run at a time rather than racing.
        services.TryAddSingleton<ILinkedTaskSync>(provider => provider.GetRequiredService<LinkedTaskSyncWorker>());

        // Write-back rides on the same registration: it needs the same connectors and
        // the same connected targets, so a head that syncs linked tasks can finish
        // them at the source, and one that does not leaves the save asking nobody.
        services.AddScoped<ICommandHandler<CompleteLinkedTaskCommand, Result<LinkedTaskWriteBackOutcome>>, CompleteLinkedTaskCommandHandler>();
        services.TryAddSingleton<ILinkedTaskWriteBack, LinkedTaskWriteBack>();

        return services;
    }
}
