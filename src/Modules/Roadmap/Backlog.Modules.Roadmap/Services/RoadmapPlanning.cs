using Backlog.Modules.Roadmap.Abstractions;
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
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Services;

/// <summary>
/// The published <see cref="IRoadmapPlanning"/> port, wired to the feature slices
/// behind it. Deliberately nothing but mapping: every rule lives in a handler or in
/// the plan, so there is no third place to look.
/// </summary>
internal sealed class RoadmapPlanning(
    IQueryHandler<GetPlanQuery, RoadmapPlanDto> getPlan,
    ICommandHandler<AddItemCommand, Result<RoadmapItemDto>> addItem,
    ICommandHandler<RescheduleItemCommand, Result<RoadmapItemDto>> rescheduleItem,
    ICommandHandler<UpdateItemCommand, Result<RoadmapItemDto>> updateItem,
    ICommandHandler<PrioritiseItemCommand, Result<RoadmapItemDto>> prioritiseItem,
    ICommandHandler<RemoveItemCommand, Result> removeItem,
    ICommandHandler<AddMilestoneCommand, Result<RoadmapMilestoneDto>> addMilestone,
    ICommandHandler<UpdateMilestoneCommand, Result<RoadmapMilestoneDto>> updateMilestone,
    ICommandHandler<RemoveMilestoneCommand, Result> removeMilestone,
    ICommandHandler<AddDependencyCommand, Result> addDependency,
    ICommandHandler<RemoveDependencyCommand, Result> removeDependency) : IRoadmapPlanning
{
    public Task<RoadmapPlanDto> GetPlanAsync(CancellationToken cancellationToken = default) =>
        getPlan.Handle(new GetPlanQuery(), cancellationToken);

    public Task<Result<RoadmapItemDto>> AddItemAsync(
        string title,
        DateOnly start,
        DateOnly end,
        PlanningPriority priority = PlanningPriority.Medium,
        IReadOnlyList<string>? repositoryAliases = null,
        string? lane = null,
        Guid? taskId = null,
        string? notes = null,
        string? tag = null,
        IReadOnlyList<string>? knowledgeRefs = null,
        CancellationToken cancellationToken = default) =>
        addItem.Handle(
            new AddItemCommand(title, start, end, priority, repositoryAliases, lane, taskId, notes, tag, knowledgeRefs),
            cancellationToken);

    public Task<Result<RoadmapItemDto>> UpdateItemAsync(
        Guid itemId,
        string title,
        DateOnly start,
        DateOnly end,
        PlanningPriority priority,
        IReadOnlyList<string>? repositoryAliases = null,
        string? lane = null,
        Guid? taskId = null,
        string? notes = null,
        string? tag = null,
        IReadOnlyList<string>? knowledgeRefs = null,
        CancellationToken cancellationToken = default) =>
        updateItem.Handle(
            new UpdateItemCommand(itemId, title, start, end, priority, repositoryAliases, lane, taskId, notes, tag, knowledgeRefs),
            cancellationToken);

    public Task<Result<RoadmapItemDto>> RescheduleItemAsync(
        Guid itemId,
        DateOnly start,
        DateOnly end,
        string? lane = null,
        CancellationToken cancellationToken = default) =>
        rescheduleItem.Handle(new RescheduleItemCommand(itemId, start, end, lane), cancellationToken);

    public Task<Result<RoadmapItemDto>> PrioritiseItemAsync(
        Guid itemId,
        PlanningPriority priority,
        CancellationToken cancellationToken = default) =>
        prioritiseItem.Handle(new PrioritiseItemCommand(itemId, priority), cancellationToken);

    public Task<Result> RemoveItemAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        removeItem.Handle(new RemoveItemCommand(itemId), cancellationToken);

    public Task<Result<RoadmapMilestoneDto>> AddMilestoneAsync(
        string title,
        DateOnly on,
        MilestoneKind kind = MilestoneKind.Release,
        IReadOnlyList<string>? repositoryAliases = null,
        string? lane = null,
        bool isPlanWide = false,
        CancellationToken cancellationToken = default) =>
        addMilestone.Handle(
            new AddMilestoneCommand(title, on, kind, repositoryAliases, lane, isPlanWide),
            cancellationToken);

    public Task<Result<RoadmapMilestoneDto>> UpdateMilestoneAsync(
        Guid milestoneId,
        string title,
        DateOnly on,
        MilestoneKind kind,
        IReadOnlyList<string>? repositoryAliases = null,
        string? lane = null,
        bool isPlanWide = false,
        CancellationToken cancellationToken = default) =>
        updateMilestone.Handle(
            new UpdateMilestoneCommand(milestoneId, title, on, kind, repositoryAliases, lane, isPlanWide),
            cancellationToken);

    public Task<Result> RemoveMilestoneAsync(Guid milestoneId, CancellationToken cancellationToken = default) =>
        removeMilestone.Handle(new RemoveMilestoneCommand(milestoneId), cancellationToken);

    public Task<Result> AddDependencyAsync(
        Guid nodeId,
        Guid dependsOnId,
        CancellationToken cancellationToken = default) =>
        addDependency.Handle(new AddDependencyCommand(nodeId, dependsOnId), cancellationToken);

    public Task<Result> RemoveDependencyAsync(
        Guid nodeId,
        Guid dependsOnId,
        CancellationToken cancellationToken = default) =>
        removeDependency.Handle(new RemoveDependencyCommand(nodeId, dependsOnId), cancellationToken);
}
