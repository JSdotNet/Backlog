using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Features.DeleteTask;
using Backlog.Modules.Tasks.Features.ImportPlan;
using Backlog.Modules.Tasks.Features.LinkTaskToIssue;
using Backlog.Modules.Tasks.Features.ListTasks;
using Backlog.Modules.Tasks.Features.RecordTaskUsage;
using Backlog.Modules.Tasks.Features.ReorderTasks;
using Backlog.Modules.Tasks.Features.SaveTaskFromText;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Tasks.Extensions;

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
public static class TasksModuleRegistration
{
    public static IServiceCollection AddTasksModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IQueryHandler<ListTasksQuery, IReadOnlyList<TaskItemDto>>, ListTasksQueryHandler>();
        services.AddScoped<ICommandHandler<SaveTaskFromTextCommand, Result<SavedTaskDto>>, SaveTaskFromTextCommandHandler>();
        services.AddScoped<ICommandHandler<LinkTaskToIssueCommand, Result<TaskItemDto>>, LinkTaskToIssueCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteTaskCommand>, DeleteTaskCommandHandler>();
        services.AddScoped<ICommandHandler<ReorderTasksCommand>, ReorderTasksCommandHandler>();
        services.AddScoped<ICommandHandler<RecordTaskUsageCommand>, RecordTaskUsageCommandHandler>();
        services.AddScoped<ICommandHandler<ImportPlanCommand, Result<ImportPlanResultDto>>, ImportPlanCommandHandler>();

        services.AddScoped<ITaskItems, TaskItems>();

        return services;
    }
}
