using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Features.AddDependency;
using Backlog.Modules.Roadmap.Features.AddItem;
using Backlog.Modules.Roadmap.Features.AddMilestone;
using Backlog.Modules.Roadmap.Features.RemoveMilestone;
using Backlog.Modules.Roadmap.Features.UpdateMilestone;
using Backlog.Modules.Roadmap.Features.GetPlan;
using Backlog.Modules.Roadmap.Features.ImportPlanItems;
using Backlog.Modules.Roadmap.Features.KeepUpWithWork;
using Backlog.Modules.Roadmap.Features.PrioritiseItem;
using Backlog.Modules.Roadmap.Features.RemoveDependency;
using Backlog.Modules.Roadmap.Features.RemoveItem;
using Backlog.Modules.Roadmap.Features.PinItemEnd;
using Backlog.Modules.Roadmap.Features.RescheduleItem;
using Backlog.Modules.Roadmap.Features.UpdateItem;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddScoped<ICommandHandler<PinItemEndCommand, Result<RoadmapItemDto>>, PinItemEndCommandHandler>();
        services.AddScoped<ICommandHandler<UnpinItemEndCommand, Result<RoadmapItemDto>>, UnpinItemEndCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateItemCommand, Result<RoadmapItemDto>>, UpdateItemCommandHandler>();
        services.AddScoped<ICommandHandler<PrioritiseItemCommand, Result<RoadmapItemDto>>, PrioritiseItemCommandHandler>();
        services.AddScoped<ICommandHandler<RemoveItemCommand, Result>, RemoveItemCommandHandler>();
        services.AddScoped<ICommandHandler<AddMilestoneCommand, Result<RoadmapMilestoneDto>>, AddMilestoneCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateMilestoneCommand, Result<RoadmapMilestoneDto>>, UpdateMilestoneCommandHandler>();
        services.AddScoped<ICommandHandler<RemoveMilestoneCommand, Result>, RemoveMilestoneCommandHandler>();
        services.AddScoped<ICommandHandler<AddDependencyCommand, Result>, AddDependencyCommandHandler>();
        services.AddScoped<ICommandHandler<RemoveDependencyCommand, Result>, RemoveDependencyCommandHandler>();
        // Needs IPlanningVelocity, registered below over the host's pace settings and
        // finished work.
        services.AddScoped<ICommandHandler<ImportPlanItemsCommand, Result<PlanImportResultDto>>, ImportPlanItemsCommandHandler>();
        // The keep-up projection's writer: the importer's own placement rule, so it needs
        // IPlanningVelocity and IRoadmapItemRollup as Import does.
        services.AddScoped<ICommandHandler<KeepUpWithWorkCommand, IReadOnlyList<RoadmapItemScheduledDto>>, KeepUpWithWorkCommandHandler>();

        // Placement reads "today"; a host that already registered a clock keeps its own.
        services.TryAddSingleton(TimeProvider.System);

        // The reader's paces, typed and measured. Needs IPlanningVelocitySettings and
        // IRoadmapCompletedWork, which the host answers through the cross-context
        // adapters. One instance answers both the view's port and the importer's.
        services.AddScoped<PlanningPace>();
        services.AddScoped<IPlanningPace>(sp => sp.GetRequiredService<PlanningPace>());
        services.AddScoped<IPlanningVelocity>(sp => sp.GetRequiredService<PlanningPace>());

        // One for the whole host, whatever scope a write or a listener comes from.
        services.TryAddSingleton<RoadmapPlanChanges>();
        // Every command above that loads and saves the plan holds this from load to save,
        // so the keep-up writer and an import started by the same task write cannot save
        // over each other. One for the whole host, for the same reason.
        services.TryAddSingleton<RoadmapPlanGate>();
        services.AddScoped<IRoadmapPlanning, RoadmapPlanning>();

        // The plan and the pace as the documents that travel between devices (local
        // ADR 0018). A singleton, because it holds the plan's change notice and the
        // pace settings' — both singletons — for a sync loop to hear. The stores are
        // the host's: whichever it registers are the documents this head keeps, and a
        // head that registers none keeps neither, so a copy of either reads as
        // unreadable rather than failing the provider.
        services.TryAddSingleton<IRoadmapReplication>(sp => new RoadmapReplication(
            sp.GetServices<IRoadmapReplicaStore>(),
            sp.GetRequiredService<RoadmapPlanChanges>(),
            sp.GetService<IPlanningVelocitySettings>(),
            sp.GetRequiredService<RoadmapPlanGate>()));

        return services;
    }
}
