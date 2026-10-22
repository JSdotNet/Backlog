namespace Backlog.Desktop.UI.Devbook;

/// <summary>Which side of the focus the Dependencies view shows.</summary>
public enum TechnologyDirection
{
    Both,
    SitsOn,
    UsedBy,
}

/// <summary>
/// One technology the focus reaches along one direction: at its nearest hop, with
/// the further hops it is also reached at, and — from two hops — the technology
/// one hop nearer it is reached through.
/// </summary>
/// <param name="Node">The technology reached.</param>
/// <param name="Hop">The nearest hop it is reached at, from 1.</param>
/// <param name="OtherHops">Every further hop a chain of <c>depends-on</c> also reaches it at, ascending.</param>
/// <param name="Via">The technology one hop nearer the focus it is first reached
/// through; null at hop 1.</param>
public sealed record TechnologyReach(TechnologyGraphNode Node, int Hop, IReadOnlyList<int> OtherHops, TechnologyGraphNode? Via);

/// <summary>One layer's share of a breaking change: how many of the technologies
/// that break sit in it.</summary>
public sealed record TechnologyLayerImpact(string Layer, int Count);

/// <summary>
/// One technology in context, as the Dependencies view reads it
/// (<c>.devbook/domain/devbook/features.md#technology-view</c>): what it sits on
/// and what uses it, hop by hop, how far a breaking change reaches by layer, and
/// its paths down to a foundation.
///
/// <para>Every walk runs over the <see cref="TechnologyGraphNode.DependencyIds"/>
/// and <see cref="TechnologyGraphNode.UserIds"/> the measures already resolved, so
/// the view counts nothing the reader did not. Each walk is bounded — a hop count
/// never exceeds the node count, a path never revisits a node — so a cycle the
/// metadata permits ends every walk rather than looping it.</para>
/// </summary>
public sealed class TechnologyDependencyMap
{
    /// <summary>The most paths to a foundation the view lists.</summary>
    public const int MaxPaths = 10;

    /// <summary>How many steps the path search takes before it stops looking, so a
    /// lattice of cycles with few foundations cannot run it for long.</summary>
    private const int PathSearchBudget = 10_000;

    private readonly Dictionary<string, TechnologyGraphNode> _nodes;

    private TechnologyDependencyMap(TechnologyGraphData graph, TechnologyGraphNode focus)
    {
        _nodes = new Dictionary<string, TechnologyGraphNode>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            _nodes.TryAdd(node.Id, node);
        }

        Focus = focus;
        SitsOn = Walk(graph, focus, node => node.DependencyIds);
        UsedBy = Walk(graph, focus, node => node.UserIds);
        Impact = [.. UsedBy
            .GroupBy(reach => reach.Node.Layer, StringComparer.Ordinal)
            .Select(layer => (Layer: layer.Key, Count: layer.Count(), Order: layer.Min(reach => LayerOrder(reach.Node))))
            .OrderByDescending(layer => layer.Count)
            .ThenBy(layer => layer.Order)
            .Select(layer => new TechnologyLayerImpact(layer.Layer, layer.Count))];
        (PathsToFoundation, HasMorePaths) = Paths(focus);
        // The chain down is walked around the chain up, so a focus inside a cycle
        // never lists a technology twice.
        var above = LongestChain(focus, node => node.UserIds, []);
        var below = LongestChain(focus, node => node.DependencyIds, [.. above.Skip(1).Select(node => node.Id)]);
        LongestPathThrough = [.. Enumerable.Reverse(above).SkipLast(1), .. below];
    }

    /// <summary>The map around <paramref name="focusId"/>; null when the graph has no such node.</summary>
    public static TechnologyDependencyMap? For(TechnologyGraphData graph, string? focusId)
    {
        var focus = focusId is null ? null : graph.Nodes.FirstOrDefault(node => string.Equals(node.Id, focusId, StringComparison.Ordinal));
        return focus is null ? null : new TechnologyDependencyMap(graph, focus);
    }

    public TechnologyGraphNode Focus { get; }

    /// <summary>What the focus sits on, each once at its nearest hop, nearest first.</summary>
    public IReadOnlyList<TechnologyReach> SitsOn { get; }

    /// <summary>What uses the focus, each once at its nearest hop, nearest first.</summary>
    public IReadOnlyList<TechnologyReach> UsedBy { get; }

    /// <summary>Everything that breaks if the focus changes — every technology that
    /// uses it at any distance — counted by layer, most first.</summary>
    public IReadOnlyList<TechnologyLayerImpact> Impact { get; }

    /// <summary>How many technologies a breaking change reaches; the measure's
    /// <em>Breaks if it changes</em>, counted here from the same edges.</summary>
    public int ImpactTotal => UsedBy.Count;

    /// <summary>Each simple path from the focus down to a technology with nothing
    /// under it, in declaration order, at most <see cref="MaxPaths"/>.</summary>
    public IReadOnlyList<IReadOnlyList<TechnologyGraphNode>> PathsToFoundation { get; }

    /// <summary>More paths exist than <see cref="PathsToFoundation"/> lists.</summary>
    public bool HasMorePaths { get; }

    /// <summary>The longest chain running through the focus: its longest chain of
    /// users above it, then its longest chain of <c>depends-on</c> below it.</summary>
    public IReadOnlyList<TechnologyGraphNode> LongestPathThrough { get; }

    /// <summary>The hops a direction reaches, nearest first; empty when it reaches nothing.</summary>
    public IReadOnlyList<int> Hops(TechnologyDirection direction) =>
        [.. (direction == TechnologyDirection.UsedBy ? UsedBy : SitsOn).Select(reach => reach.Hop).Distinct().Order()];

    /// <summary>
    /// Breadth by hop: hop <c>k</c> holds every node a chain of <c>k</c> edges
    /// reaches. A node is listed once, at the first hop it appears; every later
    /// hop it appears at is recorded beside it. Through a cycle a node reappears
    /// forever, so the hops stop at the node count — no simple chain is longer.
    /// </summary>
    private IReadOnlyList<TechnologyReach> Walk(TechnologyGraphData graph, TechnologyGraphNode focus, Func<TechnologyGraphNode, IReadOnlyList<string>> next)
    {
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < graph.Nodes.Count; index++)
        {
            order.TryAdd(graph.Nodes[index].Id, index);
        }

        var nearest = new Dictionary<string, (int Hop, string? Via)>(StringComparer.Ordinal);
        var further = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var frontier = new List<string> { focus.Id };

        for (var hop = 1; hop < graph.Nodes.Count && frontier.Count > 0; hop++)
        {
            var reached = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var from in frontier.OrderBy(id => order.GetValueOrDefault(id, int.MaxValue)))
            {
                if (!_nodes.TryGetValue(from, out var fromNode)) continue;

                foreach (var to in next(fromNode))
                {
                    if (string.Equals(to, focus.Id, StringComparison.Ordinal) || !_nodes.ContainsKey(to)) continue;
                    reached.TryAdd(to, from);
                }
            }

            foreach (var (id, via) in reached)
            {
                if (nearest.ContainsKey(id))
                {
                    if (!further.TryGetValue(id, out var hops)) further[id] = hops = [];
                    hops.Add(hop);
                }
                else
                {
                    nearest[id] = (hop, hop == 1 ? null : via);
                }
            }

            frontier = [.. reached.Keys];
        }

        return [.. nearest
            .OrderBy(pair => pair.Value.Hop)
            .ThenBy(pair => order.GetValueOrDefault(pair.Key, int.MaxValue))
            .Select(pair => new TechnologyReach(
                _nodes[pair.Key],
                pair.Value.Hop,
                further.TryGetValue(pair.Key, out var hops) ? hops : [],
                pair.Value.Via is { } via ? _nodes[via] : null))];
    }

    /// <summary>Depth first in declaration order, a node never twice on one path,
    /// stopping once one path more than the list shows has been found — or once
    /// the search budget is spent, which counts as more.</summary>
    private (IReadOnlyList<IReadOnlyList<TechnologyGraphNode>> Paths, bool More) Paths(TechnologyGraphNode focus)
    {
        var paths = new List<IReadOnlyList<TechnologyGraphNode>>();
        var path = new List<TechnologyGraphNode> { focus };
        var onPath = new HashSet<string>(StringComparer.Ordinal) { focus.Id };
        var more = false;
        var steps = 0;

        void Visit(TechnologyGraphNode node)
        {
            if (more) return;
            if (++steps > PathSearchBudget)
            {
                more = true;
                return;
            }

            var below = node.DependencyIds.Where(_nodes.ContainsKey).ToList();
            if (below.Count == 0)
            {
                if (path.Count < 2) return;
                if (paths.Count == MaxPaths)
                {
                    more = true;
                    return;
                }

                paths.Add([.. path]);
                return;
            }

            foreach (var id in below)
            {
                if (!onPath.Add(id)) continue;

                path.Add(_nodes[id]);
                Visit(_nodes[id]);
                path.RemoveAt(path.Count - 1);
                onPath.Remove(id);
            }
        }

        Visit(focus);
        return (paths, more);
    }

    /// <summary>The longest chain from <paramref name="start"/> along
    /// <paramref name="next"/>, <paramref name="start"/> first, never through
    /// <paramref name="excluded"/>; on a tie the first in declaration order.
    /// Memoised per node, so a stack shaped like a lattice is walked once rather
    /// than once per path. On an acyclic graph it is the longest; through a cycle
    /// the memo can settle on a shorter one, but the chain stays simple.</summary>
    private List<TechnologyGraphNode> LongestChain(TechnologyGraphNode start, Func<TechnologyGraphNode, IReadOnlyList<string>> next, IEnumerable<string> excluded)
    {
        var memo = new Dictionary<string, List<TechnologyGraphNode>>(StringComparer.Ordinal);
        var onPath = new HashSet<string>(excluded, StringComparer.Ordinal);

        List<TechnologyGraphNode> From(TechnologyGraphNode node)
        {
            if (memo.TryGetValue(node.Id, out var known)) return known;

            onPath.Add(node.Id);
            List<TechnologyGraphNode> best = [];
            foreach (var id in next(node))
            {
                if (onPath.Contains(id) || !_nodes.TryGetValue(id, out var onward)) continue;

                var chain = From(onward);
                if (chain.Count > best.Count) best = chain;
            }

            onPath.Remove(node.Id);
            var result = new List<TechnologyGraphNode>(best.Count + 1) { node };
            result.AddRange(best);
            memo[node.Id] = result;
            return result;
        }

        return From(start);
    }

    /// <summary>The reading order, a boundary layer after every layer of the folder.</summary>
    private static int LayerOrder(TechnologyGraphNode node) => node.LayerIndex < 0 ? int.MaxValue : node.LayerIndex;
}
