using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Tasks' <see cref="IRoadmapTagSource"/> from the roadmap
/// plan.
/// <para>
/// This is the join, and it lives in an adapter on purpose: the backlog UI may not
/// reach into Roadmap Planning, so it asks its own port and this adapter — which is
/// allowed to see both — reads the plan through <see cref="IRoadmapPlanning"/> and
/// hands back the tags in use. The same arrangement <c>KnowledgeFolderSource</c>
/// uses to answer two contexts over one lookup.
/// </para>
/// </summary>
public sealed class RoadmapPlanTagSource : IRoadmapTagSource
{
    private readonly IRoadmapPlanning _planning;

    public RoadmapPlanTagSource(IRoadmapPlanning planning)
    {
        ArgumentNullException.ThrowIfNull(planning);
        _planning = planning;
    }

    public async Task<IReadOnlyList<string>> TagsInUseAsync(CancellationToken cancellationToken = default)
    {
        var plan = await _planning.GetPlanAsync(cancellationToken);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tags = new List<string>();

        // First appearance wins, so the offered order is stable between reads — the
        // plan lists its items in one order and the picker should not shuffle them.
        foreach (var item in plan.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Tag)) continue;
            if (seen.Add(item.Tag)) tags.Add(item.Tag);
        }

        return tags;
    }
}
