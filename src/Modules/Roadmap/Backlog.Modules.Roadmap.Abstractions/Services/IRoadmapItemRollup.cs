using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.Abstractions.Services;

/// <summary>
/// What a roadmap item has gathered, and the effort registered against it.
/// <para>
/// A port on Roadmap Planning's own surface, answered by an infrastructure adapter
/// that can see both Tasks and the knowledge folders — the join a
/// screen may not make for itself
/// (<c>ModuleBoundaryTests.A_module_ui_asks_only_its_own_modules_published_surface</c>).
/// The band renders one context and asks this; the adapter reads the backlog and
/// the knowledge graph.
/// </para>
/// <para>
/// It gathers, it does not estimate. Everything in the result is a value someone
/// registered — a backlog entry's effort, a knowledge chapter's — summed as
/// arithmetic. Nothing here infers a number for work that carries none.
/// </para>
/// </summary>
public interface IRoadmapItemRollup
{
    /// <summary>
    /// Gathers the backlog entries and knowledge chapters this item reaches — its
    /// direct links and everything carrying its tag — and rolls up their effort.
    /// </summary>
    Task<RoadmapItemRollupDto> GatherAsync(RoadmapItemDto item, CancellationToken cancellationToken = default);
}
