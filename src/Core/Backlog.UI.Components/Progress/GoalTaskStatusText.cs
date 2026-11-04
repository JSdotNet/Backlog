using Backlog.UI.Components.Feedback;

namespace Backlog.UI.Components.Progress;

/// <summary>
/// How a task's status is dotted and said on the Goals view: the open goal's Tasks column
/// and No goal's task rows read the same tone and the same words, so the two cannot drift.
/// </summary>
internal static class GoalTaskStatusText
{
    /// <summary>The <see cref="StatusDot"/> tone a status draws.</summary>
    public static FlowStepTone Tone(GoalTaskStatus status) => status switch
    {
        GoalTaskStatus.Done => FlowStepTone.Done,
        GoalTaskStatus.Doing => FlowStepTone.Active,
        GoalTaskStatus.Blocked => FlowStepTone.Blocked,
        _ => FlowStepTone.Pending
    };

    /// <summary>What a screen reader hears after the title, since the dot alone carries
    /// nothing to it.</summary>
    public static string Words(GoalTaskStatus status) => status switch
    {
        GoalTaskStatus.Done => "done",
        GoalTaskStatus.Doing => "in progress",
        GoalTaskStatus.Blocked => "blocked",
        _ => "not started"
    };

    /// <summary>The <c>data-status</c> slug a row carries.</summary>
    public static string Slug(GoalTaskStatus status) => status.ToString().ToLowerInvariant();
}
