using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Abstractions;

/// <summary>
/// Every way working with sprints can be refused, named once. The codes are the
/// ones <c>.devbook/domain/progress/domain.invariants.md</c> gives each rule, so a
/// screen can match on them rather than on a message.
/// </summary>
public static class ProgressErrors
{
    /// <summary>The <see cref="ProgressFeatures.Progress"/> switch is off.</summary>
    public static Error Off() =>
        Error.Validation("progress.off", "Progress is switched off. Turn it on in Settings to work with sprints.");

    public static Error SeriesNotFound(Guid seriesId) =>
        Error.NotFound("progress.series_not_found", $"There is no sprint series {seriesId}.");

    public static Error SprintNotFound(int number) =>
        Error.NotFound("progress.sprint_not_found", $"This series has no sprint {number}.");

    public static Error SeriesWithoutRepository() =>
        Error.Validation("progress.series_without_repository", "A sprint series needs at least one repository.");

    public static Error RepositoryRequired() =>
        Error.Validation("progress.repository_required", "Name the repository to add.");

    public static Error RepositoryInOtherSeries(string alias) =>
        Error.Conflict(
            "progress.repository_in_other_series",
            $"'{alias}' already belongs to another sprint series. A repository belongs to one series only.");

    public static Error LengthNotAllowed(int days) =>
        Error.Validation(
            "progress.length_not_allowed",
            $"A sprint lasts one week or two weeks, not {days} days.");

    public static Error SprintAlreadyOpen(int number) =>
        Error.Conflict(
            "progress.sprint_already_open",
            $"Sprint {number} is still open. Close it before another one starts.");

    public static Error WindowOffCadence(DayOfWeek weekday, int days) =>
        Error.Validation(
            "progress.window_off_cadence",
            $"A sprint of this series starts on a {weekday} and lasts {days} days.");

    public static Error SprintsOverlap(DateOnly previousEnd) =>
        Error.Conflict(
            "progress.sprints_overlap",
            $"The sprint before it runs until {previousEnd:d MMM yyyy}, so the new one has to start after that.");

    public static Error SprintsNotContiguous(DateOnly expectedStart) =>
        Error.Conflict(
            "progress.sprints_not_contiguous",
            $"The sprints of a series follow each other without a gap, so the next one starts on {expectedStart:d MMM yyyy}.");

    public static Error SprintClosed(int number) =>
        Error.Conflict("progress.sprint_closed", $"Sprint {number} is closed, and a closed sprint does not change.");

    public static Error UnsettledItems(IReadOnlyList<Guid> taskIds) =>
        Error.Validation(
            "progress.unsettled_item",
            $"Choose Carry, Back to the backlog or Drop for every unfinished item first ({taskIds.Count} without one).");

    public static Error NoteNotOneLine() =>
        Error.Validation("progress.note_not_one_line", "A note on a sprint item is one line.");

    public static Error PatternWithoutNumber() =>
        Error.Validation(
            "progress.pattern_without_number",
            "A sprint name pattern holds {n} once, where the sprint's number goes, and some text besides.");

    public static Error ShareOutOfRange(int share) =>
        Error.Validation(
            "progress.share_out_of_range",
            $"The goal is met at a whole percentage from 1 to 100, not {share}.");

    public static Error GoalRuleWithoutChoice(GoalRuleKind kind) =>
        Error.Validation(
            "progress.goal_rule_without_choice",
            kind == GoalRuleKind.InPlans
                ? "A goal rule on plans names at least one plan."
                : "A goal rule on tags names at least one tag.");

    public static Error PlanTagRequired() =>
        Error.Validation("progress.plan_tag_required", "Name the plan by its plan tag.");

    public static Error NotAnItem(Guid taskId) =>
        Error.NotFound("progress.not_an_item", $"Task {taskId} is not in this sprint.");

    // --- Goals (.devbook/domain/progress/domain.invariants.md#goal) ---------------

    /// <summary>The <see cref="ProgressFeatures.Goals"/> switch is off.</summary>
    public static Error GoalsOff() =>
        Error.Validation("progress.goals_off", "Goals are switched off. Turn them on in Settings to work with goals.");

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
