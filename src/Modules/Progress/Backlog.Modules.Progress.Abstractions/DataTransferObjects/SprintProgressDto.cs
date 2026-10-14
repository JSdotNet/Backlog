namespace Backlog.Modules.Progress.Abstractions.DataTransferObjects;

/// <summary>
/// One sprint read against its members' current state
/// (<c>.devbook/domain/progress/domain.md#sprint-progress</c>). Worked out afresh on
/// every reading and never stored.
/// </summary>
/// <param name="SprintGoal">The sprint's goal in words.</param>
/// <param name="Members">Every member task, goal tasks first, then in title order.</param>
/// <param name="GoalRules">The series' goal rules that picked the goal tasks.</param>
/// <param name="GoalTaskIds">The goal set: the members one of the goal rules picks,
/// each once.</param>
/// <param name="Goal">The counts over the goal tasks alone.</param>
/// <param name="AllWork">The counts over every member.</param>
/// <param name="GoalShare">The whole percentage of goal tasks that must be done.</param>
/// <param name="GoalMet">Whether the share of done goal tasks reaches
/// <paramref name="GoalShare"/>. A sprint with no goal task has not met its goal.</param>
/// <param name="WorkingDays">Monday to Friday in the sprint's window.</param>
/// <param name="WorkingDaysElapsed">Working days from the start to today, or to the end
/// once the sprint is over, both inclusive.</param>
/// <param name="DonePerWorkingDay">Members done inside the window so far, divided by
/// <paramref name="WorkingDaysElapsed"/>; zero before the first working day.</param>
/// <param name="ReadOn">The day the reading was made — the "today" every count above is
/// as of. Last and defaulted, so a caller written before it still constructs one.</param>
public sealed record SprintProgressDto(
    Guid SeriesId,
    string SeriesName,
    int Number,
    string Name,
    DateOnly Start,
    DateOnly End,
    string SprintGoal,
    bool IsOpen,
    IReadOnlyList<SprintMemberDto> Members,
    IReadOnlyList<GoalRuleDto> GoalRules,
    IReadOnlyList<Guid> GoalTaskIds,
    SprintCountsDto Goal,
    SprintCountsDto AllWork,
    int GoalShare,
    bool GoalMet,
    int WorkingDays,
    int WorkingDaysElapsed,
    double DonePerWorkingDay,
    DateOnly? ReadOn = null);

/// <summary>
/// One member task of a sprint, with what Tasks says about it and what the sprint
/// records about it.
/// </summary>
/// <param name="Status">The task's status as Tasks has it.</param>
/// <param name="State">Where the member stands for the counts.</param>
/// <param name="PlanTag">The plan the task is filed under, without the sigil, or null.</param>
/// <param name="Repository">The repository the task belongs to, by alias, or null.</param>
/// <param name="DoneOn">The day the member was done: the task's completion day, or the
/// last merge when its pull requests made it done.</param>
/// <param name="AddedAfterStart">Whether the member joined the sprint after its first
/// day.</param>
/// <param name="Owners">The authors of the linked pull requests; empty when none is
/// known. There is no owner field.</param>
/// <param name="StartedOn">The day work on the task first moved to in progress, when
/// Tasks knows it.</param>
/// <param name="BlockedSince">The day the task was marked blocked, while it is.</param>
/// <param name="JoinedOn">The day the member joined the sprint: the day it was added
/// directly or a plan brought it in, or the sprint's first day.</param>
/// <remarks>The last three are defaulted, so a caller written before them still
/// constructs one.</remarks>
public sealed record SprintMemberDto(
    Guid TaskId,
    string Title,
    SprintTaskStatus Status,
    SprintWorkState State,
    string? PlanTag,
    string? Repository,
    DateOnly? DoneOn,
    IReadOnlyList<LinkedPullRequestDto> PullRequests,
    IReadOnlyList<string> Owners,
    bool IsGoal,
    bool AddedDirectly,
    bool AddedAfterStart,
    string? Note,
    bool OpenQuestion,
    CloseOutcome? Outcome,
    DateOnly? StartedOn = null,
    DateOnly? BlockedSince = null,
    DateOnly? JoinedOn = null);

/// <summary>How many members are in each state, and how many joined after the
/// start. <see cref="Total"/> is the sum of the four states.</summary>
public sealed record SprintCountsDto(int Done, int InProgress, int Blocked, int NotStarted, int AddedAfterStart)
{
    public int Total => Done + InProgress + Blocked + NotStarted;
}
