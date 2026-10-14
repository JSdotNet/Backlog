namespace Backlog.Modules.Progress.Abstractions.DataTransferObjects;

/// <summary>A sprint series as anything outside the module sees it
/// (<c>.devbook/domain/progress/domain.md#sprint-series</c>).</summary>
/// <param name="Repositories">The repository aliases the series belongs to, in the
/// order they were added.</param>
/// <param name="NamePattern">The text a sprint's name is made from, with <c>{n}</c>
/// for its number.</param>
/// <param name="GoalShare">The whole percentage of goal tasks that must be done for
/// a sprint's goal to be met; 100 is every goal task.</param>
/// <param name="Sprints">Every sprint of the series, by number.</param>
/// <param name="IsOn">Whether the series is switched on. A series switched off keeps
/// everything it holds, and its repositories read as in no series
/// (<c>.devbook/domain/progress/domain.md#series-switch</c>). Last and defaulted, so a
/// caller written before the switch still constructs one.</param>
public sealed record SprintSeriesDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> Repositories,
    SprintLength Length,
    DayOfWeek StartWeekday,
    string NamePattern,
    bool ShowInDailyBrief,
    bool ShowInWeeklyBrief,
    IReadOnlyList<GoalRuleDto> GoalRules,
    int GoalShare,
    IReadOnlyList<SprintDto> Sprints,
    bool IsOn = true)
{
    /// <summary>The open sprint, or null before the first one starts.</summary>
    public SprintDto? OpenSprint => Sprints.LastOrDefault(sprint => sprint.IsOpen);
}

/// <summary>One goal rule of a series. <paramref name="Choices"/> holds the plan tags
/// of an <see cref="GoalRuleKind.InPlans"/> rule and the task tags of a
/// <see cref="GoalRuleKind.Tagged"/> one, and is empty for the other two.</summary>
public sealed record GoalRuleDto(GoalRuleKind Kind, IReadOnlyList<string> Choices)
{
    public static GoalRuleDto Everything() => new(GoalRuleKind.Everything, []);

    public static GoalRuleDto MarkedAsGoal() => new(GoalRuleKind.MarkedAsGoal, []);

    public static GoalRuleDto InPlans(params string[] planTags) => new(GoalRuleKind.InPlans, planTags);

    public static GoalRuleDto Tagged(params string[] tags) => new(GoalRuleKind.Tagged, tags);
}

/// <summary>One sprint of a series (<c>.devbook/domain/progress/domain.md#sprint</c>).
/// <paramref name="Name"/> is the series' pattern with the number filled in; it is not
/// stored.</summary>
public sealed record SprintDto(
    int Number,
    string Name,
    DateOnly Start,
    DateOnly End,
    string Goal,
    bool IsOpen,
    IReadOnlyList<PlanLinkDto> PlanLinks,
    IReadOnlyList<SprintItemDto> Items);

/// <summary>A whole plan linked to a sprint by its plan tag, and the day it was
/// linked.</summary>
public sealed record PlanLinkDto(string PlanTag, DateOnly LinkedOn);

/// <summary>What a sprint records about one task
/// (<c>.devbook/domain/progress/domain.md#sprint-item</c>).</summary>
/// <param name="AddedOn">The day the task was added directly, or null for a task only a
/// plan brought in.</param>
/// <param name="Outcome">What closing the sprint did with the task, once it closed with
/// the task unfinished.</param>
public sealed record SprintItemDto(
    Guid TaskId,
    bool AddedDirectly,
    DateOnly? AddedOn,
    bool IsGoal,
    string? Note,
    bool OpenQuestion,
    CloseOutcome? Outcome);
