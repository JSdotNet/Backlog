namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The eight families the thirteen technology types fall into, so the Technology
/// views colour a stack in eight hues rather than thirteen
/// (<c>.devbook/domain/devbook/features.md#technology-view</c>). <see cref="Other"/>
/// holds what no family names: a legacy <c>kind</c>, a chapter that states no type,
/// a boundary node documented in another folder.
/// </summary>
public enum TechnologyTypeFamily
{
    Framework,
    Platform,
    Runtime,
    LanguageAndPackage,
    LibraryToolAndPlugin,
    Skill,
    AgentServiceAndFormat,
    Protocol,
    Other,
}

public static class TechnologyTypeFamilies
{
    /// <summary>The family a chapter's <c>type</c> belongs to, case aside.</summary>
    public static TechnologyTypeFamily Of(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        "framework" => TechnologyTypeFamily.Framework,
        "platform" => TechnologyTypeFamily.Platform,
        "runtime" => TechnologyTypeFamily.Runtime,
        "language" or "package" => TechnologyTypeFamily.LanguageAndPackage,
        "library" or "tool" or "plugin" => TechnologyTypeFamily.LibraryToolAndPlugin,
        "skill" => TechnologyTypeFamily.Skill,
        "agent" or "service" or "format" => TechnologyTypeFamily.AgentServiceAndFormat,
        "protocol" => TechnologyTypeFamily.Protocol,
        _ => TechnologyTypeFamily.Other,
    };

    /// <summary>The family as the chapter names it.</summary>
    public static string Label(TechnologyTypeFamily family) => family switch
    {
        TechnologyTypeFamily.Framework => "Framework",
        TechnologyTypeFamily.Platform => "Platform",
        TechnologyTypeFamily.Runtime => "Runtime",
        TechnologyTypeFamily.LanguageAndPackage => "Language and package",
        TechnologyTypeFamily.LibraryToolAndPlugin => "Library, tool and plugin",
        TechnologyTypeFamily.Skill => "Skill",
        TechnologyTypeFamily.AgentServiceAndFormat => "Agent, service and format",
        TechnologyTypeFamily.Protocol => "Protocol",
        _ => "Other",
    };
}

public enum TechnologyComplexityBand
{
    Low,
    Medium,
    High,
}

public static class TechnologyComplexity
{
    /// <summary>The lowest score banded Medium.</summary>
    public const int MediumFrom = 6;

    /// <summary>The lowest score banded High.</summary>
    public const int HighFrom = 10;

    /// <summary>Low below 6, Medium from 6 to 9, High from 10.</summary>
    public static TechnologyComplexityBand BandFor(int score) => score switch
    {
        >= HighFrom => TechnologyComplexityBand.High,
        >= MediumFrom => TechnologyComplexityBand.Medium,
        _ => TechnologyComplexityBand.Low,
    };
}

/// <summary>How many <c>depends-on</c> edges run from technologies in one layer to
/// technologies in another. A layer is named by its title, the
/// <see cref="TechnologyGraphNode.Layer"/> every node carries.</summary>
public sealed record TechnologyLayerLink(string FromLayer, string ToLayer, int Count);

/// <summary>
/// The measures of <c>.devbook/domain/devbook/features.md#technology-view</c>,
/// computed once over the graph the reader built so every view reads the same
/// numbers.
///
/// <para>Every walk keeps a visited set, so a cycle — which the metadata permits
/// and the stack should not have — ends every walk rather than looping it. Cycles
/// are found as strongly connected components and reported on the graph; a node
/// never counts itself, however the cycle comes back round to it.</para>
///
/// <para>Depth needs one more rule, because inside a cycle the longest chain has no
/// end: a cycle counts as one step, its members sharing the depth of the longest
/// chain that leaves it, and a hop between two members is not a hop down the
/// stack. On an acyclic graph every node is its own step and nothing changes.</para>
///
/// <para>The walks run over every edge the graph draws, boundary nodes included:
/// a node documented in another folder is a foundation for the walk, and counts
/// in what rests on it. The per-type and per-status counts are of this folder's
/// own technologies only.</para>
/// </summary>
internal static class TechnologyGraphMeasures
{
    public static TechnologyGraphData Apply(IReadOnlyList<TechnologyGraphNode> nodes, IReadOnlyList<TechnologyGraphEdge> edges)
    {
        var count = nodes.Count;
        var position = new Dictionary<string, int>(count, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < count; index++)
        {
            position.TryAdd(nodes[index].Id, index);
        }

        // Distinct, self excluded: a target named twice is one dependency, and a
        // technology naming itself is a cycle, not its own user.
        var dependencies = new List<int>[count];
        var users = new List<int>[count];
        var selfLoop = new bool[count];
        for (var index = 0; index < count; index++)
        {
            dependencies[index] = [];
            users[index] = [];
        }

        foreach (var edge in edges)
        {
            if (!position.TryGetValue(edge.Source, out var from) || !position.TryGetValue(edge.Target, out var to)) continue;

            if (from == to)
            {
                selfLoop[from] = true;
                continue;
            }

            if (dependencies[from].Contains(to)) continue;

            dependencies[from].Add(to);
            users[to].Add(from);
        }

        foreach (var list in users)
        {
            list.Sort();
        }

        var component = StronglyConnectedComponents(dependencies);
        var componentSize = new int[count];
        foreach (var id in component)
        {
            componentSize[id]++;
        }

        var inCycle = new bool[count];
        for (var index = 0; index < count; index++)
        {
            inCycle[index] = selfLoop[index] || componentSize[component[index]] > 1;
        }

        var depth = Depths(dependencies, component, componentSize.Count(size => size > 0));

        var measured = new TechnologyGraphNode[count];
        for (var index = 0; index < count; index++)
        {
            var restsOn = Reach(index, dependencies);
            measured[index] = nodes[index] with
            {
                DependencyIds = [.. dependencies[index].Select(target => nodes[target].Id)],
                UserIds = [.. users[index].Select(source => nodes[source].Id)],
                UsedBy = users[index].Count,
                BreaksIfItChanges = Reach(index, users),
                RestsOn = restsOn,
                Depth = depth[index],
                Complexity = restsOn + depth[index],
                ComplexityBand = TechnologyComplexity.BandFor(restsOn + depth[index]),
                Family = TechnologyTypeFamilies.Of(nodes[index].Kind),
                InCycle = inCycle[index],
            };
        }

        var technologies = measured.Where(node => !node.IsBoundary).ToList();

        return new TechnologyGraphData(measured, edges)
        {
            LayerLinks = LayerLinks(measured, dependencies),
            CountsByType = CountBy(technologies, node => node.Kind, StringComparer.OrdinalIgnoreCase),
            CountsByFamily = CountBy(technologies, node => node.Family, EqualityComparer<TechnologyTypeFamily>.Default),
            CountsByStatus = CountBy(technologies, node => node.Status, StringComparer.OrdinalIgnoreCase),
            LongestChain = LongestChain(measured, dependencies, component, depth),
            Cycles = Cycles(measured, component, componentSize, selfLoop),
        };
    }

    /// <summary>How many nodes <paramref name="start"/> reaches along
    /// <paramref name="adjacency"/>, each once, itself never.</summary>
    private static int Reach(int start, List<int>[] adjacency)
    {
        var visited = new HashSet<int> { start };
        var pending = new Stack<int>(adjacency[start]);

        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (!visited.Add(next)) continue;

            foreach (var onward in adjacency[next])
            {
                if (!visited.Contains(onward)) pending.Push(onward);
            }
        }

        return visited.Count - 1;
    }

    /// <summary>Tarjan's algorithm, iterative so a deep stack cannot overflow the
    /// thread's. Answers each node's component number.</summary>
    private static int[] StronglyConnectedComponents(List<int>[] dependencies)
    {
        var count = dependencies.Length;
        var index = new int[count];
        var low = new int[count];
        var onStack = new bool[count];
        var component = new int[count];
        Array.Fill(index, -1);

        var stack = new Stack<int>();
        var nextIndex = 0;
        var nextComponent = 0;

        for (var root = 0; root < count; root++)
        {
            if (index[root] != -1) continue;

            var frames = new Stack<(int Node, int Edge)>();
            frames.Push((root, 0));
            index[root] = low[root] = nextIndex++;
            stack.Push(root);
            onStack[root] = true;

            while (frames.Count > 0)
            {
                var (node, edge) = frames.Pop();

                if (edge < dependencies[node].Count)
                {
                    frames.Push((node, edge + 1));
                    var target = dependencies[node][edge];

                    if (index[target] == -1)
                    {
                        index[target] = low[target] = nextIndex++;
                        stack.Push(target);
                        onStack[target] = true;
                        frames.Push((target, 0));
                    }
                    else if (onStack[target])
                    {
                        low[node] = Math.Min(low[node], index[target]);
                    }

                    continue;
                }

                if (low[node] == index[node])
                {
                    int member;
                    do
                    {
                        member = stack.Pop();
                        onStack[member] = false;
                        component[member] = nextComponent;
                    }
                    while (member != node);

                    nextComponent++;
                }

                if (frames.Count > 0)
                {
                    var parent = frames.Peek().Node;
                    low[parent] = Math.Min(low[parent], low[node]);
                }
            }
        }

        return component;
    }

    /// <summary>The longest chain of hops from each node down to a node with no
    /// dependency. A cycle is one step: its members share a depth, the longest
    /// reached through any member's dependency outside it, so a hop inside the
    /// cycle is never counted. Tarjan numbers a component only after every
    /// component it reaches, so walking components in number order settles each
    /// depth before anything above it asks.</summary>
    private static int[] Depths(List<int>[] dependencies, int[] component, int componentCount)
    {
        var componentDepth = new int[componentCount];
        var order = Enumerable.Range(0, dependencies.Length).OrderBy(node => component[node]);

        foreach (var node in order)
        {
            foreach (var target in dependencies[node])
            {
                if (component[target] == component[node]) continue;
                componentDepth[component[node]] = Math.Max(componentDepth[component[node]], componentDepth[component[target]] + 1);
            }
        }

        return [.. component.Select(id => componentDepth[id])];
    }

    /// <summary>The ids along the longest chain: the deepest node first — the first
    /// in graph order on a tie — then at each step a dependency one shallower, again
    /// the first in graph order. Where the step leaves a cycle through another of
    /// its members, that member is listed too, so the chain reads unbroken; its
    /// hops are then more than the depth it starts from by the steps inside cycles.</summary>
    private static IReadOnlyList<string> LongestChain(
        IReadOnlyList<TechnologyGraphNode> nodes,
        List<int>[] dependencies,
        int[] component,
        int[] depth)
    {
        if (nodes.Count == 0) return [];

        var current = 0;
        for (var index = 1; index < nodes.Count; index++)
        {
            if (depth[index] > depth[current]) current = index;
        }

        var chain = new List<string> { nodes[current].Id };
        while (depth[current] > 0)
        {
            var from = current;
            var step = Enumerable.Range(0, nodes.Count)
                .Where(member => component[member] == component[from])
                .OrderBy(member => member == from ? -1 : member)
                .SelectMany(member => dependencies[member]
                    .Where(target => component[target] != component[from] && depth[target] == depth[from] - 1)
                    .Order()
                    .Select(target => (Member: member, Target: target)))
                .Select(step => ((int Member, int Target)?)step)
                .FirstOrDefault();

            // Depth is the longest outside step plus one, so one always exists; the
            // guard only keeps a broken invariant from becoming an endless walk.
            if (step is not { } next) break;

            if (next.Member != from) chain.Add(nodes[next.Member].Id);
            chain.Add(nodes[next.Target].Id);
            current = next.Target;
        }

        return chain;
    }

    private static IReadOnlyList<IReadOnlyList<string>> Cycles(
        IReadOnlyList<TechnologyGraphNode> nodes,
        int[] component,
        int[] componentSize,
        bool[] selfLoop) =>
        [.. Enumerable.Range(0, nodes.Count)
            .Where(node => selfLoop[node] || componentSize[component[node]] > 1)
            .GroupBy(node => component[node])
            .Select(members => (IReadOnlyList<string>)[.. members.Select(node => nodes[node].Id)])];

    private static IReadOnlyList<TechnologyLayerLink> LayerLinks(IReadOnlyList<TechnologyGraphNode> nodes, List<int>[] dependencies)
    {
        // Titles compared exactly, as the key compares them: every one is read from
        // the same heading, so two spellings of one layer do not arise.
        var links = new Dictionary<(string From, string To), (int FromOrder, int ToOrder, int Count)>();

        for (var from = 0; from < nodes.Count; from++)
        {
            foreach (var to in dependencies[from])
            {
                var key = (nodes[from].Layer, nodes[to].Layer);
                if (string.Equals(key.Item1, key.Item2, StringComparison.Ordinal)) continue;

                links[key] = links.TryGetValue(key, out var link)
                    ? link with { Count = link.Count + 1 }
                    : (LayerOrder(nodes[from]), LayerOrder(nodes[to]), 1);
            }
        }

        return [.. links
            .OrderBy(link => link.Value.FromOrder)
            .ThenBy(link => link.Value.ToOrder)
            .ThenBy(link => link.Key.From, StringComparer.Ordinal)
            .ThenBy(link => link.Key.To, StringComparer.Ordinal)
            .Select(link => new TechnologyLayerLink(link.Key.From, link.Key.To, link.Value.Count))];
    }

    /// <summary>The reading order, a boundary layer after every layer of the folder.</summary>
    private static int LayerOrder(TechnologyGraphNode node) => node.LayerIndex < 0 ? int.MaxValue : node.LayerIndex;

    private static IReadOnlyDictionary<TKey, int> CountBy<TKey>(
        IEnumerable<TechnologyGraphNode> nodes,
        Func<TechnologyGraphNode, TKey> key,
        IEqualityComparer<TKey> comparer)
        where TKey : notnull
    {
        var counts = new Dictionary<TKey, int>(comparer);
        foreach (var node in nodes)
        {
            counts[key(node)] = counts.GetValueOrDefault(key(node)) + 1;
        }

        return counts;
    }
}
