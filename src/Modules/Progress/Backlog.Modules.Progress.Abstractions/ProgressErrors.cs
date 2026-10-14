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
}
