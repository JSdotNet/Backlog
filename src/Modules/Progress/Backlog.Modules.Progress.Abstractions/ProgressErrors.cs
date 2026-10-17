using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Abstractions;

/// <summary>
/// Every way working with goals and impediments can be refused, named once. The codes are the
/// ones <c>.devbook/domain/progress/domain.invariants.md</c> gives each rule, so a
/// screen can match on them rather than on a message.
/// </summary>
public static class ProgressErrors
{
    // --- Goals (.devbook/domain/progress/domain.invariants.md#goal) ---------------

    /// <summary>The <see cref="ProgressFeatures.Goals"/> switch is off.</summary>
    public static Error GoalsOff() =>
        Error.Validation("progress.goals_off", "Goals are switched off. Turn them on in Settings to work with goals.");

    public static Error PlanTagRequired() =>
        Error.Validation("progress.plan_tag_required", "Name the plan by its plan tag.");

    public static Error GoalNotFound(Guid goalId) =>
        Error.NotFound("progress.goal_not_found", $"There is no goal {goalId}.");

    public static Error GoalWithoutTitle() =>
        Error.Validation("progress.goal_without_title", "A goal needs a title.");

    public static Error GoalWithoutDueDate() =>
        Error.Validation("progress.goal_without_due_date", "A goal needs a due date.");

    public static Error OutcomeNotOneLine() =>
        Error.Validation("progress.outcome_not_one_line", "A goal's outcome is one line.");

    public static Error PullRequestWithoutRepository() =>
        Error.Validation(
            "progress.pull_request_without_repository",
            "A pull request is named by its repository and a number above zero.");

    public static Error TaskRequired() =>
        Error.Validation("progress.task_required", "Name the task by its id.");

    public static Error AttachmentKindNotAllowed(GoalAttachmentKind kind) =>
        Error.Validation("progress.attachment_kind_not_allowed", $"A goal holds plans, tasks and pull requests, not '{kind}'.");

    /// <summary>The item is another goal's (<c>.devbook/domain/progress/requirements.md#requirement-an-item-belongs-to-at-most-one-goal</c>);
    /// the message names that goal, as the requirement asks.</summary>
    public static Error ItemInOtherGoal(string goalTitle) =>
        Error.Conflict("progress.item_in_other_goal", $"Already in {goalTitle}");

    // --- Impediments (.devbook/domain/progress/domain.invariants.md#impediment) ---

    public static Error ImpedimentNotFound(Guid impedimentId) =>
        Error.NotFound("progress.impediment_not_found", $"There is no impediment {impedimentId}.");

    /// <summary>An edit the phone asked for that the impediment did not take: it already says
    /// so, or the field no longer says what the phone saw.</summary>
    public static Error ImpedimentEditNotApplied(Guid impedimentId) =>
        Error.Conflict("progress.impediment_edit_not_applied", $"Impediment {impedimentId} did not take the edit from the phone.");

    public static Error ImpedimentWithoutTitle() =>
        Error.Validation("progress.impediment_without_title", "An impediment needs a title.");

    /// <summary>A move the state order does not allow: backwards, to the state it is in, or
    /// skipping a state other than Escalated.</summary>
    public static Error ImpedimentMoveNotAllowed(ImpedimentState from, ImpedimentState to) =>
        Error.Conflict(
            "progress.impediment_move_not_allowed",
            $"An impediment moves forward from New to Reported, Escalated and Being handled; {from} cannot move to {to}.");

    public static Error ImpedimentResolved() =>
        Error.Conflict(
            "progress.impediment_resolved",
            "A resolved impediment stays resolved. Raise a new one when the blocker comes back.");

    /// <summary>A move dated before the impediment's last step: its history's days never go
    /// backwards.</summary>
    public static Error ImpedimentMoveBeforeLastStep(DateOnly lastStep) =>
        Error.Validation(
            "progress.impediment_move_before_last_step",
            $"The impediment's last step was on {lastStep:d MMM yyyy}, so a move cannot be dated before it.");
}
