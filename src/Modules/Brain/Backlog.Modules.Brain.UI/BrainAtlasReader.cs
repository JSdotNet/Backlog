using Backlog.Desktop.UI.Devbook;
using Backlog.Modules.Brain.Abstractions;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Modules.Brain.UI;

/// <summary>
/// Turns the Brain's graph rows into an atlas: each <c>repo:</c> link gets a node
/// of its own, then Devbook's <see cref="DevbookAtlasReader.Read(DevbookAtlasScope, IReadOnlyList{DevbookAtlasSourceNode}, IReadOnlyList{DevbookAtlasSourceEdge})"/>
/// draws the map, so the Brain's atlas is the same map a repository's is.
/// </summary>
internal static class BrainAtlasReader
{
    /// <summary>What a repository chapter's folder is written as before it is
    /// shown, so the group it falls in is the alias as written rather than the
    /// alias titleised. The same prefix Devbook's reader names a group by.</summary>
    private const string RepositoryFolderPrefix = "repo:";

    /// <summary>
    /// The Brain's atlas from its graph rows: the notes and the links between
    /// them, and one node per repository chapter a <c>repo:</c> link names, the
    /// link drawn to it as an edge labelled with the repository alias and dashed
    /// where <paramref name="resolve"/> says this machine cannot follow it.
    /// <para>
    /// The builder stores a <c>repo:</c> link as an edge with no node behind its
    /// target — <c>related</c> from a note's or chapter's <c>meta</c>,
    /// <c>repo-link</c> from its prose (local ADR 0027 §6). Here each target gets
    /// its node: one per alias and chapter, whichever layout's spelling a note
    /// used and whatever case it wrote the alias in, grouped by alias so each
    /// repository reads as one region of the map. A link that does not parse names
    /// nothing to draw it to and is dropped.
    /// </para>
    /// </summary>
    public static DevbookAtlasGraph ReadBrain(
        DevbookAtlasScope scope,
        IReadOnlyList<DevbookAtlasSourceNode> sourceNodes,
        IReadOnlyList<DevbookAtlasSourceEdge> rawEdges,
        Func<BrainRepoLink, BrainRepoLinkResolution> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);

        var nodes = new List<DevbookAtlasSourceNode>(sourceNodes);
        var edges = new List<DevbookAtlasSourceEdge>(rawEdges.Count);
        var repositoryIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // One group per repository, named as the first link to it wrote the
        // alias, so backlog/… and Backlog/… do not split it in two.
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var repositoryNodes = new Dictionary<string, DevbookAtlasRepositoryLink>(StringComparer.Ordinal);

        foreach (var edge in rawEdges)
        {
            if (!BrainRepoLink.IsRepoLink(edge.Target))
            {
                edges.Add(edge);
                continue;
            }

            if (BrainRepoLink.Parse(edge.Target) is not { } link) continue;

            // The first spelling seen names the node, and every later one is
            // pointed at that id exactly: the renderer joins edges to nodes by
            // ordinal id, so a target differing only in case would draw nothing.
            var key = RepositoryNodeId(link);
            if (!repositoryIds.TryGetValue(key, out var id))
            {
                id = key;
                repositoryIds[key] = id;

                var resolution = resolve(link);
                repositoryNodes[id] = new DevbookAtlasRepositoryLink(link.Alias, $"{BrainRepoLink.Scheme}{link.Alias}/{link.Reference}", resolution.Resolved, resolution.Problem);
                var alias = aliases.TryGetValue(link.Alias, out var seen) ? seen : aliases[link.Alias] = link.Alias;
                nodes.Add(new DevbookAtlasSourceNode(id, resolution.Label, RepositoryFolderPrefix + alias, DevbookAtlasReader.RepositoryChapterKind, string.Empty, link.Path, true));
            }

            var repository = repositoryNodes[id];

            edges.Add(new DevbookAtlasSourceEdge(DevbookAtlasReader.RepoLinkEdgeKind, edge.Source, id, link.Alias, !repository.Resolved));
        }

        var graph = DevbookAtlasReader.Read(scope, nodes, edges);
        if (!graph.Available || repositoryNodes.Count == 0) return graph;

        return graph with
        {
            Nodes =
            [
                .. graph.Nodes.Select(node => repositoryNodes.TryGetValue(node.Id, out var repository)
                    // Filled where it can be followed and a hollow ring where it
                    // cannot: the same two marks a status draws, so the dashed
                    // edge is not the only thing saying it.
                    ? node with { ToneSlug = repository.Resolved ? "active" : string.Empty, RepositoryLink = repository }
                    : node)
            ]
        };
    }

    /// <summary>One id per repository chapter: the alias, the path in the root
    /// layout's spelling, and the heading.</summary>
    private static string RepositoryNodeId(BrainRepoLink link)
    {
        var path = DevbookLayout.ConventionalPath(link.Path);
        return link.Slug is null
            ? $"{BrainRepoLink.Scheme}{link.Alias}/{path}"
            : $"{BrainRepoLink.Scheme}{link.Alias}/{path}#{link.Slug}";
    }
}
