namespace Backlog.Modules.Tasks.Abstractions;

/// <summary>
/// The lifecycle graph an entry's status moves along, and the two questions
/// anybody asks of it: may it go there, and where may it go.
/// <para>
/// The table is <c>.devbook/domain/tasks/flow.md#task-lifecycle</c> written down once.
/// It lived on <c>TaskItem</c> — the aggregate that enforces it — and moved here
/// without changing a single edge, because a second caller arrived that may not
/// see the aggregate. Local ADR 0012 §5 says the MCP <c>transition</c> tool
/// "consults it rather than carrying a copy of the graph", and the tool classes
/// sit in <c>Backlog.Infrastructure.Mcp</c>, whose own csproj forbids referencing
/// a module implementation. Published from the abstractions project, the rule is
/// reachable from both sides and still stated in exactly one place.
/// </para>
/// <para>
/// A static table rather than a method on <see cref="Services.ITaskItems"/>, and
/// that is the decision rather than an accident of where it was easiest to put.
/// Which statuses follow which is a rule, not a read: it needs no entry, no
/// store and no cancellation token, and a port method for it would invite a
/// caller to believe the answer could differ per entry or per machine. The port
/// stays the way work is done to the backlog; this stays what the backlog's
/// vocabulary means.
/// </para>
/// <para>
/// <b>It is a predicate and nothing more.</b> Nothing here mutates, validates a
/// caller's intent, or decides what a refusal should do about it —
/// <c>TaskItem.ChangeStatus</c> throws, the MCP tool answers with the kept status
/// and these next steps, and <c>TaskItem.SetStatus</c> walks past the graph
/// entirely because a person's typed <c>!done</c> is an edit rather than a
/// transition. Three callers, three reactions, one table.
/// </para>
/// </summary>
public static class EntryStatusFlow
{
    /// <summary>
    /// The graph itself: from each status, the statuses it may move to directly.
    /// <para>
    /// Private because the two methods below are the whole of what a caller
    /// needs, and an exposed dictionary is an exposed <em>mutable</em> dictionary
    /// unless somebody remembers to wrap it. The shape mirrors the mermaid
    /// diagram in <c>.devbook/domain/tasks/flow.md</c> edge for edge, so a reader can hold
    /// the two side by side and see that they agree.
    /// </para>
    /// </summary>
    private static readonly Dictionary<EntryStatus, EntryStatus[]> Transitions = new()
    {
        [EntryStatus.Draft] = [EntryStatus.Ready],
        [EntryStatus.Ready] = [EntryStatus.InProgress, EntryStatus.Draft],
        [EntryStatus.InProgress] = [EntryStatus.Done, EntryStatus.Ready],
        [EntryStatus.Done] = [EntryStatus.Archived, EntryStatus.InProgress],
        [EntryStatus.Archived] = [EntryStatus.Draft],
    };

    /// <summary>
    /// Whether an entry at <paramref name="from"/> may move directly to
    /// <paramref name="to"/>.
    /// <para>
    /// <c>from == to</c> is allowed, and that is load-bearing rather than
    /// generous: a caller asking to move an entry to the status it already has is
    /// asking for nothing, and a graph that refused it would turn every
    /// idempotent re-run — a session repeating <c>!in-progress</c>, a save that
    /// re-applies the parsed status — into an error about a move nobody made.
    /// </para>
    /// </summary>
    public static bool IsAllowed(EntryStatus from, EntryStatus to) =>
        from == to || (Transitions.TryGetValue(from, out var allowed) && Array.IndexOf(allowed, to) >= 0);

    /// <summary>
    /// The statuses an entry at <paramref name="from"/> may move to, excluding
    /// the one it is already at.
    /// <para>
    /// This is what a refusal is explained with. Answering "no" on its own leaves
    /// a caller — a person or a session — to guess at the graph, and guessing at a
    /// lifecycle is how an entry ends up somewhere nobody chose. Every status has
    /// at least one, which <c>Every_status_has_somewhere_to_go</c> holds: a
    /// dead end would strand whoever got there.
    /// </para>
    /// <para>
    /// Wrapped rather than returned bare, because <see cref="IReadOnlyList{T}"/>
    /// is a promise about the interface and not about the object behind it: hand
    /// back the stored <c>EntryStatus[]</c> and a caller can cast it back to an
    /// array and rewrite the lifecycle for the whole process. That is not
    /// hypothetical — one assignment through such a cast makes
    /// <c>IsAllowed(Ready, Archived)</c> answer true, so the MCP
    /// <c>transition</c> tool would approve a jump the graph forbids and the
    /// pane's status picker would offer it. Keeping the dictionary private buys
    /// nothing while its values escape by reference.
    /// </para>
    /// </summary>
    public static IReadOnlyList<EntryStatus> NextFrom(EntryStatus from) =>
        Transitions.TryGetValue(from, out var allowed) ? Array.AsReadOnly(allowed) : [];
}
