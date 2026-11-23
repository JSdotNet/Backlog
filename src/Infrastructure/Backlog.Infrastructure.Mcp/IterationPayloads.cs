using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// What <c>read_iterations</c> answers: the last and next iteration windows, each goal read
/// over them, what was finished in last and is planned for next, grouped by goal, the planned
/// effort against the capacity, and the open impediments
/// (<c>.devbook/domain/progress/domain.md#stand-up-reading</c>). Every enum reaches the wire
/// as a kebab-case token, as <see cref="ImpedimentTools.ToWire"/> writes the states.
/// </summary>
/// <param name="Last">The window before next, that Finished and each goal's done-in-window read.</param>
/// <param name="Next">The window holding today, or the first after it, that Planned reads.</param>
/// <param name="Goals">Every goal, in the order they were set.</param>
/// <param name="Finished">The items the goals count that were done inside last, grouped by goal.</param>
/// <param name="Planned">The items expected in next, grouped by goal, No goal last.</param>
/// <param name="PlannedEffort">The planned items' effort added up; an item without one adds 0.</param>
/// <param name="Capacity">The person's pace scaled to next's worked days, in whole points; null without a pace.</param>
/// <param name="Impediments">Every open impediment, in the order they were raised.</param>
/// <param name="ResolvedInLast">How many impediments were resolved inside last.</param>
public sealed record IterationsPayload(
    IterationWindowPayload Last,
    IterationWindowPayload Next,
    IReadOnlyList<IterationGoalPayload> Goals,
    IReadOnlyList<IterationGroupPayload> Finished,
    IReadOnlyList<IterationGroupPayload> Planned,
    int PlannedEffort,
    int? Capacity,
    IReadOnlyList<IterationImpedimentPayload> Impediments,
    int ResolvedInLast)
{
    internal static IterationsPayload From(StandUpDto reading) =>
        new(
            IterationWindowPayload.From(reading.Last),
            IterationWindowPayload.From(reading.Next),
            [.. reading.Goals.Select(IterationGoalPayload.From)],
            [.. reading.Finished.Select(IterationGroupPayload.From)],
            [.. reading.Planned.Select(IterationGroupPayload.From)],
            reading.PlannedEffort,
            reading.Capacity,
            [.. reading.Impediments.Select(IterationImpedimentPayload.From)],
            reading.ResolvedInLast);

    internal static string ToWire(GoalStanding standing) => standing switch
    {
        GoalStanding.OnTrack => "on-track",
        _ => "at-risk",
    };

    internal static string ToWire(HeldBackKind kind) => kind switch
    {
        HeldBackKind.Task => "task",
        HeldBackKind.PullRequest => "pull-request",
        _ => "plan",
    };
}

/// <summary>One iteration window: its length and its first and last day, both included.</summary>
/// <param name="Length">day, week, two-weeks or month.</param>
public sealed record IterationWindowPayload(string Length, DateOnly First, DateOnly Last)
{
    internal static IterationWindowPayload From(IterationWindowDto window) =>
        new(GoalTools.ToWire(window.Length), window.First, window.Last);
}

/// <summary>One goal read over the windows.</summary>
/// <param name="DueDate">The day it is due on: its own due date, or next's last day for a goal without one.</param>
/// <param name="DueIsIterationEnd">Whether <paramref name="DueDate"/> is next's last day because the goal has none of its own.</param>
/// <param name="Standing">on-track or at-risk.</param>
/// <param name="LandsOn">When it is expected to land, or null.</param>
/// <param name="Progress">Its counts, with the items done inside last.</param>
public sealed record IterationGoalPayload(
    Guid Id,
    string Title,
    DateOnly DueDate,
    bool DueIsIterationEnd,
    string Standing,
    DateOnly? LandsOn,
    IterationGoalProgressPayload Progress)
{
    internal static IterationGoalPayload From(StandUpGoalDto goal) =>
        new(
            goal.Id,
            goal.Title,
            goal.DueDate,
            goal.DueIsIterationEnd,
            IterationsPayload.ToWire(goal.Standing),
            goal.LandsOn,
            new IterationGoalProgressPayload(
                goal.Progress.Done,
                goal.Progress.Total,
                goal.Progress.Doing,
                goal.Progress.Blocked,
                goal.Progress.ToDo,
                goal.Progress.DoneInWindow));
}

/// <summary>How far a goal has come: done of total, how the open items stand, and how many
/// were done inside last.</summary>
public sealed record IterationGoalProgressPayload(int Done, int Total, int Doing, int Blocked, int ToDo, int DoneInWindow);

/// <summary>The items of one goal in Finished or Planned, or those no goal counts.</summary>
/// <param name="GoalId">The goal, or null for No goal.</param>
/// <param name="GoalTitle">The goal's title, or null for No goal.</param>
public sealed record IterationGroupPayload(Guid? GoalId, string? GoalTitle, IReadOnlyList<IterationItemPayload> Items)
{
    internal static IterationGroupPayload From(StandUpGroupDto group) =>
        new(group.GoalId, group.GoalTitle, [.. group.Items.Select(IterationItemPayload.From)]);
}

/// <summary>One task or pull request in Finished or Planned.</summary>
/// <param name="Kind">task — a plan's entry included — or pull-request.</param>
/// <param name="TaskId">The task's id; null for a pull request.</param>
/// <param name="Title">The task's title, or owner/name#number for a pull request.</param>
/// <param name="Repository">The task's first repository alias, or the pull request's repository.</param>
/// <param name="Number">The pull request's number; null for a task.</param>
/// <param name="Effort">The task's effort, when it carries one.</param>
/// <param name="DueOn">The task's due day, when it has one.</param>
/// <param name="DoneOn">The day it was done, when it is.</param>
public sealed record IterationItemPayload(
    string Kind,
    Guid? TaskId,
    string Title,
    string? Repository,
    int? Number,
    int? Effort,
    DateOnly? DueOn,
    DateOnly? DoneOn)
{
    internal static IterationItemPayload From(StandUpItemDto item) =>
        new(GoalTools.ToWire(item.Kind), item.TaskId, item.Title, item.Repository, item.Number, item.Effort, item.DueOn, item.DoneOn);
}

/// <summary>One open impediment.</summary>
/// <param name="State">new, reported, escalated or being-handled.</param>
/// <param name="With">Who it is with, or null while nobody was told.</param>
/// <param name="DaysOpen">The worked days from the day it was raised through today, both included.</param>
/// <param name="CheckAgain">The day to look at it again, or null.</param>
/// <param name="GoalId">The goal it holds back, or null for none.</param>
/// <param name="GoalTitle">That goal's title, or null.</param>
/// <param name="Note">Its note, or null.</param>
/// <param name="ReadyToResolve">Whether it holds back at least one item and every one is
/// settled. A reading only: a person resolves it with set_impediment_state.</param>
/// <param name="HoldsBack">Each item it holds back, in the order linked.</param>
public sealed record IterationImpedimentPayload(
    Guid Id,
    string Title,
    string State,
    string? With,
    int DaysOpen,
    DateOnly? CheckAgain,
    Guid? GoalId,
    string? GoalTitle,
    string? Note,
    bool ReadyToResolve,
    IReadOnlyList<IterationHeldBackPayload> HoldsBack)
{
    internal static IterationImpedimentPayload From(StandUpImpedimentDto impediment) =>
        new(
            impediment.Id,
            impediment.Title,
            ImpedimentTools.ToWire(impediment.State),
            impediment.With,
            impediment.DaysOpen,
            impediment.CheckAgain,
            impediment.GoalId,
            impediment.GoalTitle,
            impediment.Note,
            impediment.ReadyToResolve,
            [.. impediment.HoldsBack.Select(IterationHeldBackPayload.From)]);
}

/// <summary>One item an open impediment holds back. Which identity fields are set follows <paramref name="Kind"/>.</summary>
/// <param name="Kind">task, pull-request or plan.</param>
/// <param name="Title">The task's title, null for a task the reading does not have,
/// owner/name#number for a pull request, or +tag for a plan.</param>
/// <param name="Settled">Whether the work it names is over.</param>
public sealed record IterationHeldBackPayload(
    string Kind,
    Guid? TaskId,
    string? Repository,
    int? Number,
    string? PlanTag,
    string? Title,
    bool Settled)
{
    internal static IterationHeldBackPayload From(StandUpHeldBackItemDto held) =>
        new(
            IterationsPayload.ToWire(held.Item.Kind),
            held.Item.TaskId,
            held.Item.Repository,
            held.Item.Number,
            held.Item.PlanTag,
            held.Title,
            held.Settled);
}
