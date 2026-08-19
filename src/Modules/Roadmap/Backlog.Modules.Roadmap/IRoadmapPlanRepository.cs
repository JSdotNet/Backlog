using Backlog.Modules.Roadmap.DomainModels;

namespace Backlog.Modules.Roadmap;

/// <summary>
/// Local-first persistence for the <see cref="RoadmapPlan"/> aggregate.
/// <para>
/// Whole-plan load and whole-plan save, with no per-item operations, because the
/// plan is one consistency boundary: a dependency edge is only valid with respect
/// to every other node, so there is no useful way to save half of it. That also
/// makes the port honest about what an adapter has to guarantee — a save either
/// lands completely or leaves the previous plan intact.
/// </para>
/// <para>
/// An internal port, deliberately not registered by
/// <c>AddRoadmapModule()</c>: which adapter implements it is the host's decision,
/// the same way it is for the backlog.
/// </para>
/// </summary>
public interface IRoadmapPlanRepository
{
    /// <summary>The stored plan, or an empty one when nothing has been planned
    /// yet. Never null: a first run is not a failure.</summary>
    Task<RoadmapPlan> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the whole plan. Must be atomic from a reader's point of
    /// view — a half-written plan is worse than an out-of-date one.</summary>
    Task SaveAsync(RoadmapPlan plan, CancellationToken cancellationToken = default);
}
