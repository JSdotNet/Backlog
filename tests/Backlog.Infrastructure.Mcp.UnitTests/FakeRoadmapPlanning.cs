using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The plan, as the one read the port offers. Every other member throws, for the
/// reason <see cref="FakeTaskItems"/> gives.
/// </summary>
internal sealed class FakeRoadmapPlanning(RoadmapPlanDto plan) : IRoadmapPlanning
{
    public Task<RoadmapPlanDto> GetPlanAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(plan);
    }

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
        CancellationToken cancellationToken = default) => throw Written(nameof(AddItemAsync));

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
        CancellationToken cancellationToken = default) => throw Written(nameof(UpdateItemAsync));

    public Task<Result<RoadmapItemDto>> RescheduleItemAsync(
        Guid itemId,
        DateOnly start,
        DateOnly end,
        string? lane = null,
        CancellationToken cancellationToken = default) => throw Written(nameof(RescheduleItemAsync));

    public Task<Result<RoadmapItemDto>> PrioritiseItemAsync(
        Guid itemId,
        PlanningPriority priority,
        CancellationToken cancellationToken = default) => throw Written(nameof(PrioritiseItemAsync));

    public Task<Result> RemoveItemAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        throw Written(nameof(RemoveItemAsync));

    public Task<Result<RoadmapMilestoneDto>> AddMilestoneAsync(
        string title,
        DateOnly on,
        MilestoneKind kind = MilestoneKind.Release,
        IReadOnlyList<string>? repositoryAliases = null,
        string? lane = null,
        bool isPlanWide = false,
        CancellationToken cancellationToken = default) => throw Written(nameof(AddMilestoneAsync));

    public Task<Result<RoadmapMilestoneDto>> UpdateMilestoneAsync(
        Guid milestoneId,
        string title,
        DateOnly on,
        MilestoneKind kind,
        IReadOnlyList<string>? repositoryAliases = null,
        string? lane = null,
        bool isPlanWide = false,
        CancellationToken cancellationToken = default) => throw Written(nameof(UpdateMilestoneAsync));

    public Task<Result> RemoveMilestoneAsync(Guid milestoneId, CancellationToken cancellationToken = default) =>
        throw Written(nameof(RemoveMilestoneAsync));

    public Task<Result> AddDependencyAsync(Guid nodeId, Guid dependsOnId, CancellationToken cancellationToken = default) =>
        throw Written(nameof(AddDependencyAsync));

    public Task<Result> RemoveDependencyAsync(Guid nodeId, Guid dependsOnId, CancellationToken cancellationToken = default) =>
        throw Written(nameof(RemoveDependencyAsync));

    public Task<Result<PlanImportResultDto>> ImportPlanItemsAsync(
        IReadOnlyList<PlanImportEntryDto> entries,
        IReadOnlyList<PlanTagEffortDto>? gatheredEffort = null,
        CancellationToken cancellationToken = default) =>
        throw Written(nameof(ImportPlanItemsAsync));

    private static InvalidOperationException Written(string member) =>
        new($"A read-only MCP tool called IRoadmapPlanning.{member}.");
}
