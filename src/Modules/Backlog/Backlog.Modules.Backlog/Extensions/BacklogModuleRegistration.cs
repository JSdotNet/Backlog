using Backlog.Modules.Backlog.Abstractions.DataTransferObjects;
using Backlog.Modules.Backlog.Features.DeleteTask;
using Backlog.Modules.Backlog.Features.ImportPlan;
using Backlog.Modules.Backlog.Features.LinkTaskToIssue;
using Backlog.Modules.Backlog.Features.ListTasks;
using Backlog.Modules.Backlog.Features.ReconcileRepositoryIds;
using Backlog.Modules.Backlog.Features.RecordTaskUsage;
using Backlog.Modules.Backlog.Features.ReorderTasks;
using Backlog.Modules.Backlog.Features.SaveTaskFromText;
using Backlog.Modules.Backlog.Abstractions.Services;
using Backlog.Modules.Backlog.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Backlog.Extensions;

/// <summary>
/// The module's composition root. A host calls this once and gets every use case
/// the Backlog context offers; it never registers a handler itself, and never
/// sees the aggregate.
/// <para>
/// <see cref="ITaskRepository"/> is deliberately not registered here — it is
/// an internal port, and which adapter implements it is the host's decision
/// (today the file-system one, pointed at wherever the person keeps their
/// backlog).
/// </para>
/// </summary>
public static class BacklogModuleRegistration
{
    public static IServiceCollection AddBacklogModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IQueryHandler<ListTasksQuery, IReadOnlyList<TaskItemDto>>, ListTasksQueryHandler>();
        services.AddScoped<ICommandHandler<SaveTaskFromTextCommand, Result<SavedTaskDto>>, SaveTaskFromTextCommandHandler>();
        services.AddScoped<ICommandHandler<LinkTaskToIssueCommand, Result<TaskItemDto>>, LinkTaskToIssueCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteTaskCommand>, DeleteTaskCommandHandler>();
        services.AddScoped<ICommandHandler<ReorderTasksCommand>, ReorderTasksCommandHandler>();
        services.AddScoped<ICommandHandler<RecordTaskUsageCommand>, RecordTaskUsageCommandHandler>();
        services.AddScoped<ICommandHandler<ImportPlanCommand, Result<ImportPlanResultDto>>, ImportPlanCommandHandler>();
        services.AddScoped<ICommandHandler<ReconcileRepositoryIdsCommand, Result<int>>, ReconcileRepositoryIdsCommandHandler>();

        services.AddScoped<ITaskItems, TaskItems>();

        return services;
    }
}
