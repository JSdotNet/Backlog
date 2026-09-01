namespace Backlog.UI.Components.Tasks;

/// <summary>
/// Where a task stands once the things it waits on are taken into account.
/// <para>
/// Three states rather than two. "Not done" covers both the task you can pick up
/// now and the one you cannot, and telling those two apart is the whole reason
/// anyone writes a dependency down.
/// </para>
/// </summary>
public enum TaskReadiness
{
    /// <summary>Finished. A recorded fact rather than a conclusion, which is why
    /// it wins over everything derived below it: a task somebody marked done is
    /// done even if a step it named is still outstanding.</summary>
    Done,

    /// <summary>Nothing outstanding — every id it named belongs to a row that is
    /// finished. This is the one that can be started.</summary>
    Ready,

    /// <summary>At least one thing it named is not finished, or is not in this
    /// list at all.</summary>
    Blocked
}

/// <summary>
/// One thing a task is waiting on: the id it named, and the title of the row
/// that id turned out to be — when this list holds one.
/// </summary>
/// <param name="Id">Exactly what the task named, unchanged.</param>
/// <param name="Title">The row that id resolved to, or null when nothing in this
/// list carries it. Null is the answer rather than a failed lookup, and it is
/// why the row goes on to say the id verbatim instead of a title it does not
/// have.</param>
public sealed record TaskDependency(string Id, string? Title)
{
    /// <summary>Whether this list holds the row that was named.</summary>
    public bool Known => Title is not null;

    /// <summary>What to call it on screen: the title when there is one, the raw
    /// id when there is not. Saying the id is the honest answer — only the
    /// reader can tell whether the step is missing from this view or the id is
    /// simply wrong, and a component that guessed would hide both.</summary>
    public string Label => Title ?? Id;
}

/// <summary>What one task's dependencies work out to, for the list that drew it
/// and for a host that wants the same answer.</summary>
/// <param name="Id">The task this is about.</param>
/// <param name="Readiness">Done, ready, or blocked.</param>
/// <param name="Waiting">What is still outstanding, named. Empty on a finished
/// row: done wins, so what it once waited for is history rather than a state.</param>
/// <param name="InCycle">Whether this task is part of a dependency loop. A
/// separate fact from <paramref name="Readiness"/>, because "blocked" says a
/// chain has not got here yet and this says it never will.</param>
public sealed record TaskChainStatus(
    string Id,
    TaskReadiness Readiness,
    IReadOnlyList<TaskDependency> Waiting,
    bool InCycle);

/// <summary>
/// What a list of tasks that wait on each other adds up to.
/// <para>
/// Offered rather than applied, in the spirit of <see cref="TaskMove.ApplyTo"/>:
/// <see cref="TaskListView"/> calls this to decide what to draw and writes
/// nothing back, and a host that wants the same answer for its own reasons —
/// deciding what to run next, refusing a save — calls the same functions and
/// gets the same result. Nothing here mutates the list it was handed.
/// </para>
/// </summary>
public static class TaskChain
{
    /// <summary>
    /// Whether anything in this list declares a dependency at all.
    /// <para>
    /// What the list asks before deriving anything. A list nobody chained must
    /// render exactly the markup it rendered before chains existed — no "next"
    /// marker, no blocked circle — and the cheapest way to guarantee that is not
    /// to derive at all.
    /// </para>
    /// </summary>
    public static bool IsChain(IReadOnlyList<TaskRow> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        return tasks.Any(task => task.DependsOnList.Count > 0);
    }

    /// <summary>
    /// Every task's status, in the order the list was given.
    /// <para>
    /// One pass over the rows for the readiness and one walk of the graph for
    /// the cycles. Two rules decide the rest, and both of them are deliberate:
    /// </para>
    /// <para>
    /// A dependency naming no row in <paramref name="universe"/> leaves the task
    /// blocked, and the id is carried through verbatim so the row can say it.
    /// Dropping an unknown id would let a chain claim to be ready when the step
    /// it waits on is merely missing from this view — the one failure that looks
    /// exactly like success.
    /// </para>
    /// <para>
    /// A task marked done is done. Its outstanding dependencies are not
    /// recomputed into a contradiction, and they are not reported either: done
    /// is something a person recorded, and a component that argued with it would
    /// be telling the reader they are wrong about their own list.
    /// </para>
    /// <para>
    /// <paramref name="universe"/> is where an id is looked up; <paramref
    /// name="tasks"/> is what gets a status. The two are the same list for every
    /// caller that predates this parameter, which is why it defaults to null and
    /// falls back to <paramref name="tasks"/> the moment it is asked to resolve
    /// anything — but they need not be. A host that only draws one repository's
    /// rows still owes a reader the true state of a dependency filed under
    /// another one, and the honest way to answer that is to look the id up
    /// somewhere wider than what is on screen, not to call it unknown because
    /// the view happens to be narrower than the data.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TaskChainStatus> Resolve(
        IReadOnlyList<TaskRow> tasks,
        IReadOnlyList<TaskRow>? universe = null)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        universe ??= tasks;

        // First row wins a duplicated id. A list with two rows under one id is
        // already broken in a way this cannot fix, and throwing would take the
        // whole view down over it.
        var byId = new Dictionary<string, TaskRow>(StringComparer.Ordinal);
        foreach (var task in universe) byId.TryAdd(task.Id, task);

        var cycled = CycleMembers(tasks, byId);
        var statuses = new List<TaskChainStatus>(tasks.Count);

        foreach (var task in tasks)
        {
            var inCycle = cycled.Contains(task.Id);

            if (task.Done)
            {
                statuses.Add(new TaskChainStatus(task.Id, TaskReadiness.Done, [], inCycle));
                continue;
            }

            var waiting = new List<TaskDependency>();
            foreach (var id in task.DependsOnList)
            {
                if (byId.TryGetValue(id, out var row))
                {
                    if (!row.Done) waiting.Add(new TaskDependency(id, row.Title));
                }
                else
                {
                    waiting.Add(new TaskDependency(id, null));
                }
            }

            statuses.Add(new TaskChainStatus(
                task.Id,
                waiting.Count > 0 ? TaskReadiness.Blocked : TaskReadiness.Ready,
                waiting,
                inCycle));
        }

        return statuses;
    }

    /// <summary>
    /// Every task that can be started right now, in list order.
    /// <para>
    /// A different question from <see cref="NextReady"/>, and the two are only
    /// the same answer in a linear chain. NextReady says "what should I start
    /// first"; this says "what can I start now". A fan-out — three prompts all
    /// waiting on one step that has just been finished — is precisely the case
    /// where those diverge, and it is the case a reader most needs told: the
    /// unlock is otherwise visible only negatively, by waiting lines
    /// disappearing.
    /// </para>
    /// <para>
    /// Empty rather than null when nothing is ready, because "nothing can be
    /// started" is a list of no rows rather than a missing answer.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TaskRow> Ready(IReadOnlyList<TaskRow> tasks)
    {
        var statuses = Resolve(tasks);

        return [.. tasks.Where((_, index) => statuses[index].Readiness is TaskReadiness.Ready)];
    }

    /// <summary>
    /// The task to pick up next: the first row, in list order, that is neither
    /// finished nor waiting on anything.
    /// <para>
    /// List order rather than a ranking of its own. The order is the host's, and
    /// a helper that sorted by chain depth or by how many rows a step unblocks
    /// would be answering a question nobody asked while overruling the order the
    /// reader arranged.
    /// </para>
    /// <para>
    /// The first of <see cref="Ready"/> rather than its own walk of the statuses.
    /// Two functions answering "which rows can be started" from two loops is two
    /// places for the list-order rule to live, and the day they disagree the row
    /// wearing the "next" marker is not the row the host was told to run.
    /// </para>
    /// <para>
    /// Null when nothing is ready — which is what a cycle leaves behind, since
    /// every member of one waits on another member that is not done. Returning a
    /// member anyway would be picking a place to start in a chain that has no
    /// start, and the reader would work on it and get nowhere.
    /// </para>
    /// </summary>
    public static TaskRow? NextReady(IReadOnlyList<TaskRow> tasks) =>
        Ready(tasks).FirstOrDefault();

    /// <summary>
    /// The rows caught in a dependency loop, in list order, or none.
    /// <para>
    /// Surfaced rather than swallowed. A cycle is not a rendering problem to be
    /// worked around; it is a mistake in the data only the host can fix, and the
    /// only useful thing a component can do about one is name the rows in it.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TaskRow> Cycles(IReadOnlyList<TaskRow> tasks)
    {
        var statuses = Resolve(tasks);

        return [.. tasks.Where((_, index) => statuses[index].InCycle)];
    }

    /// <summary>
    /// Every task that can reach itself through the ids it named — Tarjan's
    /// strongly connected components, run from an explicit stack.
    /// <para>
    /// Iterative rather than recursive on purpose. A cycle is exactly the shape
    /// that turns a recursive walk into a stack overflow, and this runs over a
    /// list a host handed us: the one input it must not fall over on is the one
    /// it exists to detect.
    /// </para>
    /// <para>
    /// An id naming no row here is not followed. It still blocks the task that
    /// named it, but it cannot be part of a loop this list can see, and treating
    /// a missing row as a node would invent structure out of an absence.
    /// </para>
    /// </summary>
    private static HashSet<string> CycleMembers(
        IReadOnlyList<TaskRow> tasks,
        Dictionary<string, TaskRow> byId)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var low = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var members = new HashSet<string>(StringComparer.Ordinal);
        var component = new Stack<string>();
        var work = new Stack<(string Node, int Edge)>();
        var next = 0;

        foreach (var root in tasks)
        {
            if (index.ContainsKey(root.Id)) continue;

            index[root.Id] = low[root.Id] = next++;
            component.Push(root.Id);
            onStack.Add(root.Id);
            work.Push((root.Id, 0));

            while (work.Count > 0)
            {
                var (node, edge) = work.Pop();
                var edges = byId[node].DependsOnList;

                if (edge < edges.Count)
                {
                    // Put the node back with the next edge before descending, so
                    // the walk resumes where it left off rather than restarting.
                    work.Push((node, edge + 1));

                    var child = edges[edge];
                    if (!byId.ContainsKey(child)) continue;

                    if (!index.ContainsKey(child))
                    {
                        index[child] = low[child] = next++;
                        component.Push(child);
                        onStack.Add(child);
                        work.Push((child, 0));
                    }
                    else if (onStack.Contains(child))
                    {
                        low[node] = Math.Min(low[node], index[child]);
                    }

                    continue;
                }

                if (work.Count > 0)
                {
                    var parent = work.Peek().Node;
                    low[parent] = Math.Min(low[parent], low[node]);
                }

                if (low[node] != index[node]) continue;

                var group = new List<string>();
                string popped;
                do
                {
                    popped = component.Pop();
                    onStack.Remove(popped);
                    group.Add(popped);
                }
                while (!string.Equals(popped, node, StringComparison.Ordinal));

                // A component of one is only a cycle when the row names itself.
                // Every other single node is just a step with a predecessor.
                if (group.Count > 1 || edges.Contains(node, StringComparer.Ordinal))
                {
                    foreach (var member in group) members.Add(member);
                }
            }
        }

        return members;
    }
}
