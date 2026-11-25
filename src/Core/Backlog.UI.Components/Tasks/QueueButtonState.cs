namespace Backlog.UI.Components.Tasks;

/// <summary>
/// Which of its three looks a <see cref="QueueButton"/> wears (local ADR 0062 §4).
/// </summary>
public enum QueueButtonState
{
    /// <summary>Nothing is queued for the row: muted, and a press queues it.</summary>
    Idle,

    /// <summary>A session is queued for the row: amber on a dark amber fill, and a
    /// press takes it off the queue again.</summary>
    Queued,

    /// <summary>The row is an entry of a plan, which is queued only as a whole: dim,
    /// refused, and labelled to queue the plan instead.</summary>
    PlanEntry
}
