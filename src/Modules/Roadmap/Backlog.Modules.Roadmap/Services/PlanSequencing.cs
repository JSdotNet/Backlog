using Backlog.Modules.Roadmap.DomainModels;

namespace Backlog.Modules.Roadmap.Services;

/// <summary>
/// The questions about the plan that are about the graph rather than about any one
/// node: would this edge close a cycle, and where does the plan disagree with
/// itself.
/// <para>
/// A domain service because neither answer belongs to a single node's own state —
/// both need every node in view. See
/// <c>.domain/roadmap/domain.md#domain-service-plan-sequencing</c>.
/// </para>
/// <para>
/// Deliberately static and pure: it is handed the nodes and returns an answer,
/// which is what makes it safe for <see cref="RoadmapPlan"/> to consult while
/// deciding whether to accept a change.
/// </para>
/// </summary>
public static class PlanSequencing
{
    /// <summary>
    /// Whether making <paramref name="waitingId"/> wait for
    /// <paramref name="dependsOnId"/> would mean something ends up waiting for
    /// itself.
    /// <para>
    /// Answered by walking forward from the proposed dependency: if the thing that
    /// would now be waited for already waits — however indirectly — on the thing
    /// that would now be waiting, the edge closes a loop. The walk is breadth-first
    /// over ids and visits each node once, so a plan with a cycle already in it
    /// (which cannot happen through this module, but can through a hand-edited
    /// file) terminates rather than spinning.
    /// </para>
    /// </summary>
    public static bool WouldCycle(IEnumerable<PlanNode> nodes, Guid waitingId, Guid dependsOnId)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        if (waitingId == dependsOnId) return true;

        var dependenciesById = nodes.ToDictionary(node => node.Id, node => node.DependsOn);
        var seen = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(dependsOnId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == waitingId) return true;
            if (!seen.Add(current)) continue;
            if (!dependenciesById.TryGetValue(current, out var dependsOn)) continue;

            foreach (var next in dependsOn) queue.Enqueue(next);
        }

        return false;
    }

    /// <summary>
    /// Every place the plan's dates contradict its dependencies — a node that opens
    /// on or before the close of something it is supposed to wait for.
    /// <para>
    /// On or before, rather than strictly before, because two things that touch on
    /// the same day have not been sequenced: the second cannot start on a day the
    /// first is still running. It is also the boundary the timeline draws at, so
    /// what the reader sees and what this reports cannot disagree.
    /// </para>
    /// <para>
    /// An edge whose other end is missing is skipped rather than reported. A
    /// dangling edge is a different problem from a date that does not fit, and this
    /// module never creates one; a hand-edited file that contains one gets a plan
    /// that still reads.
    /// </para>
    /// </summary>
    public static IReadOnlyList<PlanContradiction> Contradictions(IEnumerable<PlanNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var byId = nodes.ToDictionary(node => node.Id);

        return
        [
            .. from node in byId.Values
               from dependsOnId in node.DependsOn
               where byId.ContainsKey(dependsOnId)
               let dependsOn = byId[dependsOnId]
               where node.Opens <= dependsOn.Closes
               orderby node.Title, dependsOn.Title
               select new PlanContradiction(node.Id, dependsOnId, Explain(node, dependsOn))
        ];
    }

    private static string Explain(PlanNode waiting, PlanNode dependsOn) =>
        $"'{waiting.Title}' starts {waiting.Opens:d MMM yyyy}, "
        + $"but waits for '{dependsOn.Title}' which runs until {dependsOn.Closes:d MMM yyyy}.";
}
