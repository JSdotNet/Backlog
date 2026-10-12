namespace Backlog.Desktop.UI.Inbox;

/// <summary>The decisions an Inbox session makes, as the undo history and the
/// inbox-zero screen count them.</summary>
public enum InboxDecisionKind
{
    /// <summary>Archive, and "Archive as duplicate of…".</summary>
    Archive,

    /// <summary>Defer, or a deferral's date changed.</summary>
    Defer,

    /// <summary>Move to a list, or back to the unfiled inbox.</summary>
    MoveToList,

    /// <summary>Move to backlog, across a selection or a list too, and Create plan.</summary>
    MoveToBacklog,

    /// <summary>Merge into a task.</summary>
    MergeIntoTask,

    /// <summary>Link to task….</summary>
    LinkToTask,
}

/// <summary>
/// One decision the reader can take back with U, and what taking it back
/// means. Inverse operations rather than the snapshots
/// <c>TaskUndoHistory</c> keeps, because an Inbox decision is not a text save:
/// each is a lifecycle step the module owns, and its way back is the module's
/// too — some of them (deleting the entries a route made) refuse.
/// </summary>
internal abstract record InboxUndoStep(InboxDecisionKind Kind, string Label);

/// <summary>The item was deferred when the decision was taken, until
/// <paramref name="Until"/> or with no date. Archiving or routing clears a
/// deferral, so undoing one defers the item again to be as it was.</summary>
internal sealed record InboxDeferral(DateOnly? Until);

/// <summary>An archive — plain, as a duplicate, or the merge into a task.
/// Undone by restoring the item to unprocessed, which clears
/// <c>DuplicateOf</c>, then deferring it again when it was
/// <paramref name="Deferred"/>; a merge's comment stays on the task.</summary>
internal sealed record InboxRestoreUndoStep(InboxDecisionKind Kind, string Label, Guid ItemId, InboxDeferral? Deferred = null)
    : InboxUndoStep(Kind, Label);

/// <summary>A deferral. Undone by resurfacing the item — or, when it was
/// already deferred and only its date moved, by deferring it to
/// <paramref name="PreviousUntil"/> again.</summary>
internal sealed record InboxDeferUndoStep(string Label, Guid ItemId, bool WasDeferred, DateOnly? PreviousUntil)
    : InboxUndoStep(InboxDecisionKind.Defer, Label);

/// <summary>A move to a list. Undone by moving the item back to
/// <paramref name="PreviousListId"/>, null being the unfiled inbox.</summary>
internal sealed record InboxMoveUndoStep(string Label, Guid ItemId, Guid? PreviousListId)
    : InboxUndoStep(InboxDecisionKind.MoveToList, Label);

/// <summary>A route or a link. Undone by returning the item to unprocessed —
/// with <paramref name="DeleteTasks"/> after deleting the entries the route
/// made, which the backlog refuses once one of them has started — then
/// deferring it again when it was <paramref name="Deferred"/>.</summary>
internal sealed record InboxRouteUndoStep(InboxDecisionKind Kind, string Label, Guid ItemId, bool DeleteTasks, InboxDeferral? Deferred = null)
    : InboxUndoStep(Kind, Label);

/// <summary>One gesture's decisions over several items — a bulk archive, a bulk
/// move, a batch route and the duplicates it merged — taken back together.
/// <paramref name="Kind"/> is the gesture's; each member counts under its own.</summary>
internal sealed record InboxGroupUndoStep(InboxDecisionKind Kind, string Label, IReadOnlyList<InboxUndoStep> Steps)
    : InboxUndoStep(Kind, Label);

/// <summary>
/// The Inbox session's undo history
/// (<c>.devbook/design/interaction-guidelines.md#undo-and-history</c>), modelled
/// on <c>TaskUndoHistory</c>: memory only, bounded, held by
/// <see cref="InboxDesktopState"/> for the life of the app and never persisted —
/// a history that outlived the session would be undoing against a store other
/// devices have written since.
/// <para>
/// Undo only, no redo: the decision the reader took back is one key away
/// again, and a redo of "Move to backlog" would make new entries rather than
/// bring the deleted ones back.
/// </para>
/// <para>
/// It also keeps the session's tally per decision kind, for the inbox-zero
/// screen: a decision counts once it lands and stops counting once it is
/// undone. The tally is kept apart from the steps, so a step trimmed off the
/// bottom of a long session is still counted.
/// </para>
/// </summary>
internal sealed class InboxUndoHistory
{
    public const int Capacity = 100;

    private readonly LinkedList<InboxUndoStep> _undo = new();
    private readonly Dictionary<InboxDecisionKind, int> _counts = [];

    public bool CanUndo => _undo.Count > 0;

    /// <summary>What U would take back, for a title or a test; null when
    /// there is nothing.</summary>
    public InboxUndoStep? Latest => _undo.Last?.Value;

    /// <summary>How many decisions of <paramref name="kind"/> the session has
    /// made and not taken back.</summary>
    public int CountOf(InboxDecisionKind kind) => _counts.GetValueOrDefault(kind);

    /// <summary>Every kind with a count, for the inbox-zero screen's summary.</summary>
    public IReadOnlyDictionary<InboxDecisionKind, int> Counts => _counts;

    public void Record(InboxUndoStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        _undo.AddLast(step);
        foreach (var decision in Decisions(step).Where(IsTallied)) _counts[decision.Kind] = CountOf(decision.Kind) + 1;
        while (_undo.Count > Capacity) _undo.RemoveFirst();
    }

    /// <summary>Takes the newest step off the history. The caller runs its
    /// inverse and reports with <see cref="Undone"/>; a step whose inverse was
    /// refused is not put back, because it would stand on top and refuse every
    /// U after it, hiding every older decision behind it.</summary>
    public InboxUndoStep? TakeLatest()
    {
        if (_undo.Last?.Value is not { } step) return null;

        _undo.RemoveLast();
        return step;
    }

    /// <summary>The decisions of a step that were taken back — all of a single
    /// step, or the part of a group that landed — so the tally stops counting
    /// them.</summary>
    public void Undone(IEnumerable<InboxUndoStep> decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);

        foreach (var decision in decisions.SelectMany(Decisions).Where(IsTallied))
        {
            var left = CountOf(decision.Kind) - 1;
            if (left > 0) _counts[decision.Kind] = left;
            else _counts.Remove(decision.Kind);
        }
    }

    public void Clear()
    {
        _undo.Clear();
        _counts.Clear();
    }

    /// <summary>The single decisions a step stands for: itself, or a group's
    /// members — each counted under its own kind, so a batch route's merged
    /// duplicates count as archives and its routed items as routes.</summary>
    internal static IEnumerable<InboxUndoStep> Decisions(InboxUndoStep step) =>
        step is InboxGroupUndoStep group ? group.Steps.SelectMany(Decisions) : [step];

    /// <summary>Whether a decision adds to the tally: a deferral's new date is
    /// undone like any decision, but the item was counted when it was deferred.</summary>
    private static bool IsTallied(InboxUndoStep decision) => decision is not InboxDeferUndoStep { WasDeferred: true };
}
