using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Features.CountTasks;
using Backlog.Modules.Tasks.Features.DeleteTask;
using Backlog.Modules.Tasks.Features.FileFollowUp;
using Backlog.Modules.Tasks.Features.GetTask;
using Backlog.Modules.Tasks.Features.ImportPlan;
using Backlog.Modules.Tasks.Features.LinkTaskToIssue;
using Backlog.Modules.Tasks.Features.ListTasks;
using Backlog.Modules.Tasks.Features.ReconcileRepositoryIds;
using Backlog.Modules.Tasks.Features.RefileRepositoryTasks;
using Backlog.Modules.Tasks.Features.RenameRepository;
using Backlog.Modules.Tasks.Features.RecordTaskUsage;
using Backlog.Modules.Tasks.Features.ReorderTasks;
using Backlog.Modules.Tasks.Features.SaveTaskFromText;
using Backlog.Modules.Tasks.Features.SetDevbookReferences;
using Backlog.Modules.Tasks.Features.SetPlannedHours;
using Backlog.Modules.Tasks.Features.UnlinkTaskFromIssue;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // The repository is the host's, but the signal its adapter raises is the
        // module's: one singleton, so the repository that raises it and the loop
        // that hears it are talking about the same event. TryAdd so a test that
        // composed its own keeps it.
        services.TryAddSingleton<ITaskChangeSignal, TaskChangeSignal>();

        services.AddScoped<IQueryHandler<ListTasksQuery, IReadOnlyList<TaskItemDto>>, ListTasksQueryHandler>();
        services.AddScoped<IQueryHandler<GetTaskQuery, TaskItemDto?>, GetTaskQueryHandler>();
        services.AddScoped<IQueryHandler<CountTasksQuery, int>, CountTasksQueryHandler>();
        services.AddScoped<ICommandHandler<SaveTaskFromTextCommand, Result<SavedTaskDto>>, SaveTaskFromTextCommandHandler>();
        services.AddScoped<ICommandHandler<LinkTaskToIssueCommand, Result<TaskItemDto>>, LinkTaskToIssueCommandHandler>();
        services.AddScoped<ICommandHandler<UnlinkTaskFromIssueCommand, Result<TaskItemDto>>, UnlinkTaskFromIssueCommandHandler>();
        services.AddScoped<ICommandHandler<SetDevbookReferencesCommand, Result<TaskItemDto>>, SetDevbookReferencesCommandHandler>();
        services.AddScoped<ICommandHandler<SetPlannedHoursCommand, Result<TaskItemDto>>, SetPlannedHoursCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteTaskCommand>, DeleteTaskCommandHandler>();
        services.AddScoped<ICommandHandler<ReorderTasksCommand>, ReorderTasksCommandHandler>();
        services.AddScoped<ICommandHandler<RecordTaskUsageCommand>, RecordTaskUsageCommandHandler>();
        services.AddScoped<ICommandHandler<ImportPlanCommand, Result<ImportPlanResultDto>>, ImportPlanCommandHandler>();
        services.AddScoped<ICommandHandler<ReconcileRepositoryIdsCommand, Result<int>>, ReconcileRepositoryIdsCommandHandler>();
        services.AddScoped<ICommandHandler<RenameRepositoryCommand, Result<int>>, RenameRepositoryCommandHandler>();
        services.AddScoped<ICommandHandler<RefileRepositoryTasksCommand, Result<int>>, RefileRepositoryTasksCommandHandler>();

        // The refile's drop stamps today's date; TryAdd so a host or a test that
        // registered its own clock keeps it.
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ITaskItems, TaskItems>();

        // A port of its own beside ITaskItems, for the Sessions adapter that files the
        // follow-up an Approve + follow-up answer asks for; it reads the same clock.
        services.AddScoped<ITaskFollowUps, FileFollowUpHandler>();

        return services;
    }
}
