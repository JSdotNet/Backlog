namespace Backlog.Modules.Progress.Abstractions.DataTransferObjects;

/// <summary>
/// One goal as a caller receives it (<c>.devbook/domain/progress/domain.md#goal</c>): what
/// it is for, the day it should be reached by, and the work attached to it. A snapshot:
/// how far the goal has come is <see cref="GoalSummaryDto.Progress"/>, read afresh from
/// that work on every reading.
/// </summary>
/// <param name="Outcome">One line saying what is true once the goal is reached, or null.</param>
public sealed record GoalDto(
    Guid Id,
    string Title,
    string? Outcome,
    DateOnly DueDate,
    IReadOnlyList<GoalAttachmentDto> Attachments);

/// <summary>
/// One piece of work a goal holds (<c>.devbook/domain/progress/domain.md#goal-attachment</c>).
/// Which of its fields is set follows <see cref="Item"/>'s kind.
/// </summary>
/// <param name="AttachedOn">The day it was attached.</param>
public sealed record GoalAttachmentDto(GoalItemDto Item, DateOnly AttachedOn);

/// <summary>
/// A plan, a task or a pull request, named by its identity alone — what a caller attaches,
/// detaches or asks the goal of. Build one with <see cref="ForPlan"/>, <see cref="ForTask"/> or
/// <see cref="ForPullRequest"/>.
/// </summary>
/// <param name="PlanTag">A plan's tag, with or without its <c>+</c> sigil.</param>
/// <param name="TaskId">A task's id.</param>
/// <param name="Repository">A pull request's repository, as <c>owner/name</c>.</param>
/// <param name="Number">A pull request's number.</param>
public sealed record GoalItemDto(
    GoalAttachmentKind Kind,
    string? PlanTag = null,
    Guid? TaskId = null,
    string? Repository = null,
    int? Number = null)
{
    public static GoalItemDto ForPlan(string planTag) => new(GoalAttachmentKind.Plan, PlanTag: planTag);

    public static GoalItemDto ForTask(Guid taskId) => new(GoalAttachmentKind.Task, TaskId: taskId);

    public static GoalItemDto ForPullRequest(string repository, int number) =>
        new(GoalAttachmentKind.PullRequest, Repository: repository, Number: number);
}

/// <summary>
/// How far a goal has come (<c>.devbook/domain/progress/domain.md#goal-progress</c>):
/// <c>done/total</c> over its items, each counted once, how the open ones stand, and how
/// many were done inside the window the reading asked about — the <c>+3</c> beside it.
/// </summary>
/// <param name="Doing">Open items in progress, and not blocked.</param>
/// <param name="Blocked">Open items marked blocked.</param>
/// <param name="ToDo">Open items neither in progress nor blocked.</param>
/// <param name="DoneInWindow">Items done inside the window asked about; 0 when none was.</param>
public sealed record GoalProgressDto(int Done, int Total, int Doing, int Blocked, int ToDo, int DoneInWindow);

/// <summary>A goal with its progress, as the goals list gives it.</summary>
public sealed record GoalSummaryDto(GoalDto Goal, GoalProgressDto Progress);

/// <summary>What a person gives a goal when they set it or edit it
/// (<c>.devbook/domain/progress/features.md#setting-a-goal</c>). An edit replaces all three.</summary>
/// <param name="Outcome">One line, or null or blank for none.</param>
/// <param name="DueDate">Required: a goal without one is refused.</param>
public sealed record SetGoalRequest(string Title, string? Outcome, DateOnly? DueDate);
