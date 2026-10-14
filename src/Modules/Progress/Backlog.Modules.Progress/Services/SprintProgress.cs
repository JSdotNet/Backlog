using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// Reads a sprint against its members' current state
/// (<c>.devbook/domain/progress/domain.md#sprint-progress</c>): resolves the members
/// from the direct items and the plan links, picks the goal tasks by the series' goal
/// rules, decides which are done, counts, and says whether the goal is met. Pure, and
/// stores nothing.
/// </summary>
public static class SprintProgress
{
    /// <summary>
    /// What to ask Tasks for: an open sprint's direct items by id and its plans in the
    /// series' repositories; a closed sprint's kept items by id alone, because what it
    /// held at closing is its record.
    /// </summary>
    public static (IReadOnlyCollection<Guid> TaskIds, IReadOnlyCollection<string> PlanTags) Query(Sprint sprint)
    {
        ArgumentNullException.ThrowIfNull(sprint);

        if (!sprint.IsOpen)
        {
            return ([.. sprint.Items.Where(item => item.KeptAtClose || item.AddedDirectly).Select(item => item.TaskId)], []);
        }

        return (
            [.. sprint.Items.Where(item => item.AddedDirectly).Select(item => item.TaskId)],
            [.. sprint.PlanLinks.Select(link => link.Tag)]);
    }

    /// <summary>
    /// The members of <paramref name="sprint"/> among <paramref name="tasks"/>: a direct
    /// item, a task a linked plan brings in from the series' repositories, or — once
    /// closed — a task the sprint kept at closing. Each task once.
    /// </summary>
    public static IReadOnlyList<SprintTaskDto> Members(SprintSeries series, Sprint sprint, IEnumerable<SprintTaskDto> tasks)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(sprint);
        ArgumentNullException.ThrowIfNull(tasks);

        return [.. tasks.DistinctBy(task => task.Id).Where(task => IsMember(series, sprint, task))];
    }

    /// <summary>Done by <c>.devbook/domain/progress/domain.md#done</c>: the task is Done,
    /// or it links at least one pull request and every one of them is merged. An archived
    /// task is not done by its status: archiving says nothing about whether it was
    /// finished.</summary>
    public static bool IsDone(SprintTaskDto task, IReadOnlyList<LinkedPullRequestDto> pullRequests) =>
        task.Status == SprintTaskStatus.Done
        || (pullRequests.Count > 0 && pullRequests.All(pull => pull.State == LinkedPullRequestState.Merged));

    public static SprintProgressDto Read(
        SprintSeries series,
        Sprint sprint,
        IEnumerable<SprintTaskDto> tasks,
        IReadOnlyList<LinkedPullRequestDto> pullRequests,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(pullRequests);

        var members = Members(series, sprint, tasks);
        var pullsByKey = pullRequests
            .GroupBy(pull => Key(pull.Repository, pull.Number), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var rows = members
            .Select(task => Row(series, sprint, task, PullsOf(task, pullsByKey)))
            .ToList();

        var goalIds = rows.Where(row => row.IsGoal).Select(row => row.TaskId).ToList();
        var goal = Count(rows.Where(row => row.IsGoal));
        var all = Count(rows);

        var lastCounted = today < sprint.Window.End ? today : sprint.Window.End;
        var elapsed = WorkingDaysBetween(sprint.Window.Start, lastCounted);
        var doneInWindow = rows.Count(row =>
            row.State == SprintWorkState.Done && row.DoneOn is { } day && day >= sprint.Window.Start && day <= lastCounted);

        return new SprintProgressDto(
            series.Id,
            series.Name,
            sprint.Number,
            series.NamePattern.NameOf(sprint.Number),
            sprint.Window.Start,
            sprint.Window.End,
            sprint.Goal,
            sprint.IsOpen,
            [.. rows.OrderByDescending(row => row.IsGoal).ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)],
            [.. series.GoalRules.Select(SprintSeriesMapper.ToDto)],
            goalIds,
            goal,
            all,
            series.Threshold.Share,
            series.Threshold.IsMet(goal.Done, goal.Total),
            WorkingDaysBetween(sprint.Window.Start, sprint.Window.End),
            elapsed,
            elapsed == 0 ? 0 : (double)doneInWindow / elapsed);
    }

    /// <summary>Monday to Friday from <paramref name="first"/> to <paramref name="last"/>,
    /// both inclusive; zero when <paramref name="last"/> is before <paramref name="first"/>.</summary>
    public static int WorkingDaysBetween(DateOnly first, DateOnly last)
    {
        var count = 0;
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) count++;
        }

        return count;
    }

    private static bool IsMember(SprintSeries series, Sprint sprint, SprintTaskDto task)
    {
        var item = sprint.ItemFor(task.Id);

        if (!sprint.IsOpen) return item is not null && (item.KeptAtClose || item.AddedDirectly);
        if (item is { AddedDirectly: true }) return true;

        return BroughtByPlan(series, sprint, task) is not null;
    }

    /// <summary>The earliest-linked plan that brings <paramref name="task"/> in, or null.</summary>
    private static PlanLink? BroughtByPlan(SprintSeries series, Sprint sprint, SprintTaskDto task)
    {
        if (!task.RepositoryAliases.Any(series.HasRepository)) return null;

        return sprint.PlanLinks
            .Where(link => task.PlanTags.Any(tag => PlanTags.Same(tag, link.Tag)))
            .OrderBy(link => link.LinkedOn)
            .FirstOrDefault();
    }

    private static SprintMemberDto Row(
        SprintSeries series,
        Sprint sprint,
        SprintTaskDto task,
        IReadOnlyList<LinkedPullRequestDto> pulls)
    {
        var item = sprint.ItemFor(task.Id);
        var done = IsDone(task, pulls);
        var state = done
            ? SprintWorkState.Done
            : task.IsBlocked
                ? SprintWorkState.Blocked
                : task.Status == SprintTaskStatus.InProgress ? SprintWorkState.InProgress : SprintWorkState.NotStarted;

        DateOnly? doneOn = !done
            ? null
            : task.Status == SprintTaskStatus.Done && task.CompletedOn is { } completed
                ? completed
                : pulls.Select(pull => pull.MergedOn).Max();

        return new SprintMemberDto(
            task.Id,
            task.Title,
            task.Status,
            state,
            PlanOf(sprint, task),
            task.RepositoryAliases.FirstOrDefault(series.HasRepository) ?? task.RepositoryAliases.FirstOrDefault(),
            doneOn,
            pulls,
            [.. pulls.Select(pull => pull.Author).OfType<string>().Where(author => author.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)],
            IsGoal(series, sprint, task, item),
            item?.AddedDirectly ?? false,
            JoinedOn(series, sprint, task, item) > sprint.Window.Start,
            item?.Note,
            item?.OpenQuestion ?? false,
            item?.Outcome);
    }

    /// <summary>The plan the row is grouped under: a linked plan that brings it in, else
    /// the first plan tag it carries.</summary>
    private static string? PlanOf(Sprint sprint, SprintTaskDto task) =>
        sprint.PlanLinks.Select(link => link.Tag).FirstOrDefault(tag => task.PlanTags.Any(own => PlanTags.Same(own, tag)))
        ?? task.PlanTags.Select(PlanTags.Bare).FirstOrDefault(tag => tag.Length > 0);

    private static bool IsGoal(SprintSeries series, Sprint sprint, SprintTaskDto task, SprintItem? item) =>
        series.GoalRules.Any(rule => rule.Kind switch
        {
            GoalRuleKind.Everything => true,
            GoalRuleKind.MarkedAsGoal => item?.IsGoal ?? false,
            GoalRuleKind.InPlans => task.PlanTags.Any(tag => rule.Choices.Any(choice => PlanTags.Same(tag, choice))),
            GoalRuleKind.Tagged => task.Tags.Any(tag => rule.Choices.Contains(tag.Trim(), StringComparer.OrdinalIgnoreCase)),
            _ => false
        });

    /// <summary>
    /// The day the task joined the sprint: the earliest of the day it was added
    /// directly and the day a plan brought it in — the plan's link day, or the task's
    /// creation when it was filed under the plan later. A task kept at closing whose
    /// route is gone joined at the start.
    /// </summary>
    private static DateOnly JoinedOn(SprintSeries series, Sprint sprint, SprintTaskDto task, SprintItem? item)
    {
        DateOnly? direct = item is { AddedDirectly: true } ? item.AddedOn ?? sprint.Window.Start : null;

        DateOnly? viaPlan = null;
        if (BroughtByPlan(series, sprint, task) is { } link)
        {
            viaPlan = task.CreatedOn is { } created && created > link.LinkedOn ? created : link.LinkedOn;
        }

        return (direct, viaPlan) switch
        {
            ({ } a, { } b) => a < b ? a : b,
            ({ } a, null) => a,
            (null, { } b) => b,
            _ => sprint.Window.Start
        };
    }

    private static SprintCountsDto Count(IEnumerable<SprintMemberDto> rows)
    {
        var list = rows.ToList();
        return new SprintCountsDto(
            list.Count(row => row.State == SprintWorkState.Done),
            list.Count(row => row.State == SprintWorkState.InProgress),
            list.Count(row => row.State == SprintWorkState.Blocked),
            list.Count(row => row.State == SprintWorkState.NotStarted),
            list.Count(row => row.AddedAfterStart));
    }

    private static IReadOnlyList<LinkedPullRequestDto> PullsOf(
        SprintTaskDto task,
        IReadOnlyDictionary<string, LinkedPullRequestDto> pullsByKey) =>
        [.. task.PullRequests.Select(pull =>
            pullsByKey.TryGetValue(Key(pull.Repository, pull.Number), out var read) ? read : LinkedPullRequestDto.Unread(pull))];

    private static string Key(string repository, string number) => $"{repository}#{number}";
}
