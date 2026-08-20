using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Features.AddDependency;
using Backlog.Modules.Roadmap.Features.AddItem;
using Backlog.Modules.Roadmap.Features.AddMilestone;
using Backlog.Modules.Roadmap.Features.RemoveMilestone;
using Backlog.Modules.Roadmap.Features.UpdateMilestone;
using Backlog.Modules.Roadmap.Features.GetPlan;
using Backlog.Modules.Roadmap.Features.PrioritiseItem;
using Backlog.Modules.Roadmap.Features.RemoveDependency;
using Backlog.Modules.Roadmap.Features.RemoveItem;
using Backlog.Modules.Roadmap.Features.RescheduleItem;
using Backlog.Modules.Roadmap.Features.UpdateItem;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Roadmap.Extensions;

/// <summary>
/// The module's composition root. A host calls this once and gets every use case
/// the Roadmap Planning context offers; it never registers a handler itself, and
/// never sees the plan.
/// <para>
/// <see cref="IRoadmapPlanRepository"/> is deliberately not registered here — it is
/// an internal port, and which adapter implements it is the host's decision (today
/// the file-system one, pointed at wherever the person keeps their storage).
/// </para>
/// </summary>
public static class RoadmapModuleRegistration
{
    public static IServiceCollection AddRoadmapModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IQueryHandler<GetPlanQuery, RoadmapPlanDto>, GetPlanQueryHandler>();
        services.AddScoped<ICommandHandler<AddItemCommand, Result<RoadmapItemDto>>, AddItemCommandHandler>();
        services.AddScoped<ICommandHandler<RescheduleItemCommand, Result<RoadmapItemDto>>, RescheduleItemCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateItemCommand, Result<RoadmapItemDto>>, UpdateItemCommandHandler>();
        services.AddScoped<ICommandHandler<PrioritiseItemCommand, Result<RoadmapItemDto>>, PrioritiseItemCommandHandler>();
        services.AddScoped<ICommandHandler<RemoveItemCommand, Result>, RemoveItemCommandHandler>();
        services.AddScoped<ICommandHandler<AddMilestoneCommand, Result<RoadmapMilestoneDto>>, AddMilestoneCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateMilestoneCommand, Result<RoadmapMilestoneDto>>, UpdateMilestoneCommandHandler>();
        services.AddScoped<ICommandHandler<RemoveMilestoneCommand, Result>, RemoveMilestoneCommandHandler>();
        services.AddScoped<ICommandHandler<AddDependencyCommand, Result>, AddDependencyCommandHandler>();
        services.AddScoped<ICommandHandler<RemoveDependencyCommand, Result>, RemoveDependencyCommandHandler>();

        services.AddScoped<IRoadmapPlanning, RoadmapPlanning>();

        return services;
    }
}
