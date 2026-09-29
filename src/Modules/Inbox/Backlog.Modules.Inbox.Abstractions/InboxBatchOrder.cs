using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Inbox.Abstractions;

/// <summary>One dependency between two items of a batch: <see cref="From"/>
/// comes after <see cref="To"/>.</summary>
public readonly record struct InboxBatchEdge(Guid From, Guid To);

/// <summary>
/// The order a batch's items go in once its dependencies are on, and the loops
/// that would leave no item able to go first.
/// <para>
/// Published beside <see cref="InboxPlanTag"/>, and for its reason: the pane
/// asks it again every time a toggle changes, and the route asks it once more
/// before anything is sent, so the panel and the command cannot disagree about
/// what a loop is. Pure — items and edges in, an answer out.
/// </para>
/// </summary>
public static class InboxBatchOrder
{
    /// <summary>The arrow a loop's name is drawn with: <c>A → B → A</c>.</summary>
    public const string Arrow = " → ";

    /// <summary>The in-batch edges among <paramref name="dependencies"/>: those
    /// whose target is another item rather than a task.</summary>
    public static IReadOnlyList<InboxBatchEdge> Edges(IEnumerable<ProposedDependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        return [.. dependencies
            .Where(dependency => dependency.To.Kind == DependencyTargetKind.Item)
            .Select(dependency => new InboxBatchEdge(dependency.From, dependency.To.Id))];
    }

    /// <summary>
    /// <paramref name="items"/> with every item after the ones it waits on, and
    /// otherwise in the order asked: at each step, the first item asked whose
    /// dependencies have all been placed. An edge naming an item outside the
    /// batch, or the item itself, is not a dependency here. Items caught in a
    /// loop cannot all go first, so once only they are left they go in the order
    /// asked — the order is always the whole batch, loop or not.
    /// </summary>
    public static IReadOnlyList<Guid> Order(IReadOnlyList<Guid> items, IEnumerable<InboxBatchEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(items);

        var distinct = items.Distinct().ToList();
        var waitsOn = Graph(distinct, edges);
        var placed = new HashSet<Guid>();
        var order = new List<Guid>(distinct.Count);

        while (order.Count < distinct.Count)
        {
            var ready = distinct.FindIndex(item => !placed.Contains(item) && waitsOn[item].All(placed.Contains));
            var next = ready >= 0 ? distinct[ready] : distinct.First(item => !placed.Contains(item));

            placed.Add(next);
            order.Add(next);
        }

        return order;
    }

    /// <summary>
    /// Every loop among <paramref name="edges"/>, each as the items it goes
    /// through in the direction of waiting — <c>A</c> waits on <c>B</c>, which
    /// waits on <c>A</c> — starting at the one asked first, without repeating it.
    /// <para>
    /// One loop per knot of items that all wait on each other, and that one the
    /// shortest way round from the first of them. A knot can hold several
    /// loops; naming the shortest is enough to act on, because turning one of
    /// its edges off either frees the knot or leaves the next loop to be named
    /// the next time this is asked. Ordered by where each loop starts.
    /// </para>
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Guid>> Loops(IReadOnlyList<Guid> items, IEnumerable<InboxBatchEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(items);

        var distinct = items.Distinct().ToList();
        var waitsOn = Graph(distinct, edges);
        var reach = distinct.ToDictionary(item => item, item => Reachable(item, waitsOn));
        var assigned = new HashSet<Guid>();
        var loops = new List<IReadOnlyList<Guid>>();

        foreach (var start in distinct)
        {
            if (assigned.Contains(start) || !reach[start].Contains(start)) continue;

            var knot = distinct.Where(item => reach[start].Contains(item) && reach[item].Contains(start)).ToHashSet();
            assigned.UnionWith(knot);
            loops.Add(ShortestLoop(start, knot, waitsOn));
        }

        return loops;
    }

    /// <summary>
    /// <paramref name="dependencies"/> with a merge applied: every dependency
    /// from an item folded into another (<paramref name="keptFor"/>, merged item
    /// to the one kept) is the kept item's, and every dependency on one is on the
    /// kept item — the same thought still waits on, and is waited on by, the same
    /// things. One that would then run from the kept item to itself is dropped,
    /// and each (item, target) pair is kept once, the first given. A carried
    /// target keeps the title it was proposed with.
    /// <para>
    /// Published here for the reason the order is: the panel orders and checks
    /// for loops with it, and the route does the same before sending, so the two
    /// cannot disagree about what a merge does to the order.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ProposedDependency> Carry(
        IEnumerable<ProposedDependency> dependencies,
        IReadOnlyDictionary<Guid, Guid> keptFor)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(keptFor);

        var carried = new List<ProposedDependency>();
        var seen = new HashSet<(Guid, DependencyTargetKind, Guid)>();

        foreach (var dependency in dependencies)
        {
            var from = keptFor.TryGetValue(dependency.From, out var keptFrom) ? keptFrom : dependency.From;
            var to = dependency.To.Kind == DependencyTargetKind.Item && keptFor.TryGetValue(dependency.To.Id, out var keptTo)
                ? dependency.To with { Id = keptTo }
                : dependency.To;

            if (to.Kind == DependencyTargetKind.Item && to.Id == from) continue;
            if (!seen.Add((from, to.Kind, to.Id))) continue;

            carried.Add(from == dependency.From && ReferenceEquals(to, dependency.To) ? dependency : dependency with { From = from, To = to });
        }

        return carried;
    }

    /// <summary>A loop named by its items' titles, back to where it started:
    /// <c>Write the docs → Ship the release → Write the docs</c>.</summary>
    public static string Name(IReadOnlyList<Guid> loop, Func<Guid, string> title)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(title);

        return loop.Count == 0 ? string.Empty : string.Join(Arrow, loop.Append(loop[0]).Select(title));
    }

    /// <summary>What each item waits on inside the batch, in the order the
    /// edges were given, with self-edges and strangers dropped.</summary>
    private static Dictionary<Guid, List<Guid>> Graph(List<Guid> items, IEnumerable<InboxBatchEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);

        var waitsOn = items.ToDictionary(item => item, _ => new List<Guid>());

        foreach (var edge in edges)
        {
            if (edge.From == edge.To) continue;
            if (!waitsOn.TryGetValue(edge.From, out var targets) || !waitsOn.ContainsKey(edge.To)) continue;
            if (!targets.Contains(edge.To)) targets.Add(edge.To);
        }

        return waitsOn;
    }

    /// <summary>Everything <paramref name="start"/> waits on, directly or not —
    /// itself included only when a path leads back to it.</summary>
    private static HashSet<Guid> Reachable(Guid start, Dictionary<Guid, List<Guid>> waitsOn)
    {
        var seen = new HashSet<Guid>();
        var pending = new Stack<Guid>(waitsOn[start]);

        while (pending.Count > 0)
        {
            var item = pending.Pop();
            if (!seen.Add(item)) continue;
            foreach (var next in waitsOn[item]) pending.Push(next);
        }

        return seen;
    }

    /// <summary>The fewest steps from <paramref name="start"/> back to itself
    /// inside the knot, breadth first, so ties go to the edge given first.</summary>
    private static List<Guid> ShortestLoop(Guid start, HashSet<Guid> knot, Dictionary<Guid, List<Guid>> waitsOn)
    {
        var cameFrom = new Dictionary<Guid, Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var item = queue.Dequeue();

            foreach (var next in waitsOn[item].Where(knot.Contains))
            {
                if (next == start)
                {
                    var path = new List<Guid> { item };
                    while (path[^1] != start) path.Add(cameFrom[path[^1]]);
                    path.Reverse();
                    return path;
                }

                if (cameFrom.ContainsKey(next)) continue;
                cameFrom[next] = item;
                queue.Enqueue(next);
            }
        }

        // Unreachable: every member of a knot leads back to its start.
        return [start];
    }
}
