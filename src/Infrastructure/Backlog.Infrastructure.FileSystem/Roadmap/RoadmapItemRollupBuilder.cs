using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Turns what a roadmap item reaches — its direct links and everything carrying its
/// tag — into the two de-duplicated lists a rollup shows.
/// <para>
/// A pure function of the item, the backlog and the knowledge nodes, deliberately
/// apart from the adapter that reads those. Everything worth asserting about a
/// rollup — that a thing linked and tagged is counted once, that a direct link and
/// a tag match merge, that <c>null</c> effort is left out of the sum but kept in the
/// count — is decided here, where it can be checked without a store or a file.
/// </para>
/// </summary>
public static class RoadmapItemRollupBuilder
{
    public static RoadmapItemRollupDto Build(
        RoadmapItemDto item,
        IReadOnlyList<TaskItemDto> backlog,
        IReadOnlyList<KnowledgeGraphNode> knowledge)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(backlog);
        ArgumentNullException.ThrowIfNull(knowledge);

        return new RoadmapItemRollupDto(
            RoadmapRollup.Merge(BacklogCandidates(item, backlog)),
            RoadmapRollup.Merge(KnowledgeCandidates(item, knowledge)));
    }

    private static IEnumerable<RoadmapGatheredLink> BacklogCandidates(
        RoadmapItemDto item,
        IReadOnlyList<TaskItemDto> backlog)
    {
        var tag = item.Tag;
        var hasTag = !string.IsNullOrWhiteSpace(tag);

        foreach (var entry in backlog)
        {
            var direct = item.TaskId is { } linked && entry.Id == linked;
            var tagged = hasTag && entry.Tags.Any(candidate => CarriesPlanTag(candidate, tag!));

            if (!direct && !tagged) continue;

            yield return new RoadmapGatheredLink(
                entry.Id.ToString(),
                entry.Title,
                entry.Effort,
                Origin(direct, tagged));
        }
    }

    private static IEnumerable<RoadmapGatheredLink> KnowledgeCandidates(
        RoadmapItemDto item,
        IReadOnlyList<KnowledgeGraphNode> knowledge)
    {
        var byId = new Dictionary<string, KnowledgeGraphNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in knowledge) byId.TryAdd(node.Id, node);

        // Direct references first, so their order is the person's reading order — the
        // tag matches that follow keep whatever order the graph lists them in.
        foreach (var reference in item.Knowledge)
        {
            if (string.IsNullOrWhiteSpace(reference)) continue;

            if (byId.TryGetValue(reference, out var node))
            {
                yield return new RoadmapGatheredLink(node.Id, node.Label, node.Effort, RollupOrigin.Direct);
            }
            else
            {
                // A reference to a chapter the graph does not know is an ordinary
                // state, not an error: it is shown as itself, with no effort to sum.
                yield return new RoadmapGatheredLink(reference, reference, null, RollupOrigin.Direct);
            }
        }

        var tag = item.Tag;
        if (string.IsNullOrWhiteSpace(tag)) yield break;

        foreach (var node in knowledge)
        {
            if (!node.Roadmap.Contains(tag, StringComparer.OrdinalIgnoreCase)) continue;
            yield return new RoadmapGatheredLink(node.Id, node.Label, node.Effort, RollupOrigin.Tag);
        }
    }

    /// <summary>
    /// Whether a backlog entry's stored tag names the roadmap item's slug.
    /// <para>
    /// A plan tag is stored as <c>+slug</c>, and an entry filed before the sigil
    /// existed carries the bare <c>slug</c>; both roll up, or every entry already
    /// on disk would drop off its item the day the sigil arrived. What must not roll
    /// up is a person: <c>@release-q4</c> names somebody, and only the <c>+</c> is
    /// lifted before the compare so it stays a mismatch.
    /// </para>
    /// </summary>
    private static bool CarriesPlanTag(string candidate, string slug)
    {
        var bare = candidate.StartsWith('+') ? candidate[1..] : candidate;

        return string.Equals(bare, slug, StringComparison.OrdinalIgnoreCase);
    }

    private static RollupOrigin Origin(bool direct, bool tagged) =>
        direct && tagged ? RollupOrigin.Both : direct ? RollupOrigin.Direct : RollupOrigin.Tag;
}
