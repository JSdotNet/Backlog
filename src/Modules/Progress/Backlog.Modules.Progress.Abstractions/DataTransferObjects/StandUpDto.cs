namespace Backlog.Modules.Progress.Abstractions.DataTransferObjects;

/// <summary>
/// One reading of the stand-up (<c>.devbook/domain/progress/domain.md#stand-up</c>): its two
/// windows and its four blocks, in order — Goals, Finished, Planned and Impediments.
/// </summary>
/// <param name="Last">The window before next, that Finished and the goals' <c>+n</c> read.</param>
/// <param name="Next">The window holding today, or the first after it, that Planned reads.</param>
/// <param name="Goals">Every goal, in the order they were set.</param>
/// <param name="Finished">The items done inside last, grouped by goal, No goal last.</param>
/// <param name="Planned">The items expected in next, grouped by goal, No goal last.</param>
/// <param name="PlannedEffort">The planned items' <c>effort:</c> added up; an item without one adds 0.</param>
/// <param name="Capacity">The person's pace scaled to next's worked days, in whole points;
/// null without a pace.</param>
/// <param name="Impediments">Every open impediment, in the order they were raised.</param>
/// <param name="ResolvedInLast">How many impediments were resolved inside last.</param>
public sealed record StandUpDto(
    IterationWindowDto Last,
    IterationWindowDto Next,
    IReadOnlyList<StandUpGoalDto> Goals,
    IReadOnlyList<StandUpGroupDto> Finished,
    IReadOnlyList<StandUpGroupDto> Planned,
    int PlannedEffort,
    int? Capacity,
    IReadOnlyList<StandUpImpedimentDto> Impediments,
    int ResolvedInLast);

/// <summary>An iteration window as a caller receives it: its length and its first and last day, both included.</summary>
public sealed record IterationWindowDto(IterationLength Length, DateOnly First, DateOnly Last)
{
    public static IterationWindowDto From(IterationWindow window) => new(window.Length, window.First, window.Last);
}

/// <summary>One goal in the Goals block, with its progress and forecast.</summary>
/// <param name="Progress">Its counts; <see cref="GoalProgressDto.DoneInWindow"/> is the items done inside last.</param>
/// <param name="LandsOn">When it is expected to land, or null (<see cref="GoalForecast.LandsOn"/>).</param>
public sealed record StandUpGoalDto(
    Guid Id,
    string Title,
    DateOnly DueDate,
    GoalProgressDto Progress,
    GoalStanding Standing,
    DateOnly? LandsOn);

/// <summary>The items of one goal in Finished or Planned, or those no goal counts.</summary>
/// <param name="GoalId">The goal, or null for "No goal".</param>
/// <param name="GoalTitle">The goal's title, or null for "No goal".</param>
public sealed record StandUpGroupDto(Guid? GoalId, string? GoalTitle, IReadOnlyList<StandUpItemDto> Items);

/// <summary>One task or pull request in Finished or Planned.</summary>
/// <param name="Kind"><see cref="GoalAttachmentKind.Task"/> for a task — a plan's entry
/// included — or <see cref="GoalAttachmentKind.PullRequest"/> for an attached pull request.</param>
/// <param name="TaskId">The task's id; null for a pull request.</param>
/// <param name="Title">The task's title, or <c>owner/name#number</c> for a pull request.</param>
/// <param name="Repository">The task's first repository alias, or the pull request's repository.</param>
/// <param name="Number">The pull request's number; null for a task.</param>
/// <param name="Effort">The task's <c>effort:</c>, when it carries one.</param>
/// <param name="DueOn">The task's due day, when it has one.</param>
/// <param name="DoneOn">The day it was done, when it is.</param>
public sealed record StandUpItemDto(
    GoalAttachmentKind Kind,
    Guid? TaskId,
    string Title,
    string? Repository,
    int? Number,
    int? Effort,
    DateOnly? DueOn,
    DateOnly? DoneOn);

/// <summary>One open impediment in the Impediments block.</summary>
/// <param name="With">Who it is with: who handles it, else who it was reported to; null
/// while it is New and nobody was told ("Not reported yet").</param>
/// <param name="DaysOpen">Its age: the worked days from the day it was raised through today, both included.</param>
/// <param name="GoalId">The first goal, in goal order, that counts a task it holds back; null for none.</param>
/// <param name="GoalTitle">That goal's title, or null.</param>
public sealed record StandUpImpedimentDto(
    Guid Id,
    string Title,
    ImpedimentState State,
    string? With,
    int DaysOpen,
    DateOnly? CheckAgain,
    Guid? GoalId,
    string? GoalTitle);
