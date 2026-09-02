using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Abstractions.Services;

/// <summary>
/// Everything a host may do to the plan, in one port.
/// <para>
/// The use cases themselves are feature slices with their own handlers (ADR
/// 0009); this is the service contract ADR 0005 asks a module to publish, and it
/// is a plain delegation to those handlers — the same shape as
/// <c>IBacklogEntries</c>. A screen that reads the plan and reschedules on it
/// would otherwise take a handler per gesture.
/// </para>
/// <para>
/// Note what is not here: no plan object, no repository, and no way to set a
/// field. Every method is something a person does to a plan, and each one comes
/// back as a <see cref="Result"/> when it can fail for a reason worth showing —
/// a cycle, an unknown node, dates that do not make a window.
/// </para>
/// </summary>
public interface IRoadmapPlanning
{
    /// <summary>The whole plan, with its contradictions worked out.</summary>
    Task<RoadmapPlanDto> GetPlanAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a piece of planned work. Fails when it has no title or when
    /// the dates do not make a window.</summary>
    Task<Result<RoadmapItemDto>> AddItemAsync(
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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes an edited item back — title, window, priority, repositories, lane, link
    /// and notes, all of them, every time.
    /// <para>
    /// Every field is sent even when it did not change, because a partial update
    /// cannot say the difference between "leave this alone" and "clear this", and the
    /// field that loses that argument is whichever one somebody just emptied on
    /// purpose. Dependencies are not here: they are the one thing that can be refused
    /// for a reason outside the item, so they keep their own operations.
    /// </para>
    /// </summary>
    Task<Result<RoadmapItemDto>> UpdateItemAsync(
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
        CancellationToken cancellationToken = default);

    /// <summary>Moves an item in time, and optionally to another lane. Both days
    /// are inclusive.</summary>
    Task<Result<RoadmapItemDto>> RescheduleItemAsync(
        Guid itemId,
        DateOnly start,
        DateOnly end,
        string? lane = null,
        CancellationToken cancellationToken = default);

    /// <summary>Changes the plan's own priority for an item. Never touches a
    /// linked backlog entry.</summary>
    Task<Result<RoadmapItemDto>> PrioritiseItemAsync(
        Guid itemId,
        PlanningPriority priority,
        CancellationToken cancellationToken = default);

    /// <summary>Removes an item, and every dependency that pointed at it.</summary>
    Task<Result> RemoveItemAsync(Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>Adds a fixed point to the plan.</summary>
    Task<Result<RoadmapMilestoneDto>> AddMilestoneAsync(
        string title,
        DateOnly on,
        MilestoneKind kind = MilestoneKind.Release,
        IReadOnlyList<string>? repositoryAliases = null,
        string? lane = null,
        bool isPlanWide = false,
        CancellationToken cancellationToken = default);

    /// <summary>Writes an edited milestone back: every field, in one go.</summary>
    Task<Result<RoadmapMilestoneDto>> UpdateMilestoneAsync(
        Guid milestoneId,
        string title,
        DateOnly on,
        MilestoneKind kind,
        IReadOnlyList<string>? repositoryAliases = null,
        string? lane = null,
        bool isPlanWide = false,
        CancellationToken cancellationToken = default);

    /// <summary>Takes a date off the plan, and every dependency that waited on it.</summary>
    Task<Result> RemoveMilestoneAsync(Guid milestoneId, CancellationToken cancellationToken = default);

    /// <summary>Records that one node has to land before another can. Fails when
    /// either end is unknown, when a node would wait on itself, or when the edge
    /// would close a cycle — in which case the plan is left exactly as it was.</summary>
    Task<Result> AddDependencyAsync(Guid nodeId, Guid dependsOnId, CancellationToken cancellationToken = default);

    /// <summary>Takes a dependency back out. Removing one that was never there is
    /// not an error.</summary>
    Task<Result> RemoveDependencyAsync(Guid nodeId, Guid dependsOnId, CancellationToken cancellationToken = default);
}
