using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.DomainModels;

namespace Backlog.Modules.Roadmap.Services;

/// <summary>
/// Turns the plan into what a caller receives. One place, so a handler never
/// decides how much of an item to hand out.
/// </summary>
internal static class RoadmapMapper
{
    internal static RoadmapPlanDto ToDto(this RoadmapPlan plan) => new(
        [.. plan.Items.Select(ToDto)],
        [.. plan.Milestones.Select(ToDto)],
        [.. plan.Contradictions().Select(ToDto)],
        plan.BandColours.Chosen);

    internal static RoadmapItemDto ToDto(this RoadmapItem item) => new(
        item.Id,
        item.Title,
        item.Window.Start,
        item.Window.End,
        item.Priority,
        item.Scope.Aliases,
        // The default lane is not written out. A caller that has not chosen a lane
        // should see "nobody said" rather than a label this module invented, and
        // decide for itself what an unfiled row is called.
        item.Lane.IsDefault ? null : item.Lane.Name,
        item.TaskId,
        item.Dependencies.All,
        item.Notes,
        item.Tag.Value,
        item.KnowledgeRefs.Refs);

    internal static RoadmapMilestoneDto ToDto(this Milestone milestone) => new(
        milestone.Id,
        milestone.Title,
        milestone.On,
        milestone.Kind,
        milestone.Scope.Aliases,
        milestone.Lane.IsDefault ? null : milestone.Lane.Name,
        milestone.Dependencies.All,
        milestone.IsPlanWide);

    private static PlanContradictionDto ToDto(PlanContradiction contradiction) =>
        new(contradiction.NodeId, contradiction.DependsOnId, contradiction.Reason);
}
