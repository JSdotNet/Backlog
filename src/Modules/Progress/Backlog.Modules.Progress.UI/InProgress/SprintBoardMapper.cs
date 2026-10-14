using System.Globalization;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.UI.Components.Progress;

namespace Backlog.Modules.Progress.UI.InProgress;

/// <summary>
/// Turns one sprint read against its members' current state into what the sprint view
/// draws (<c>.devbook/domain/progress/features.md#following-a-sprint</c>): the counts
/// the module worked out, kept as they are, and the words and the done line the board
/// draws them with. Pure, so its tests need no module behind them.
/// </summary>
public static class SprintBoardMapper
{
    /// <summary>The board for <paramref name="progress"/>, a sprint of
    /// <paramref name="series"/>.</summary>
    public static SprintBoardModel ToModel(SprintProgressDto progress, SprintSeriesDto series)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(series);

        var numbers = series.Sprints.Select(sprint => sprint.Number).ToList();
        var goal = progress.GoalTaskIds.ToHashSet();
        var goalPlans = progress.GoalRules
            .Where(rule => rule.Kind is GoalRuleKind.InPlans)
            .SelectMany(rule => rule.Choices)
            .Select(Bare)
            .Where(plan => plan.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SprintBoardModel(
            progress.SeriesName,
            progress.Name,
            Dates(progress.Start, progress.End),
            series.Repositories,
            numbers.Any(number => number < progress.Number),
            numbers.Any(number => number > progress.Number),
            progress.SprintGoal,
            Counts(progress.Goal),
            GoalRules(progress.GoalRules, progress.GoalShare),
            progress.GoalMet,
            Counts(progress.AllWork),
            DayText(progress),
            DonePerDay(progress),
            progress.WorkingDays,
            PaceReading(progress),
            goalPlans,
            [.. progress.Members.Select(member => Row(member, goal.Contains(member.TaskId)))]);
    }

    /// <summary>"Mon 5 Oct – Sun 18 Oct".</summary>
    public static string Dates(DateOnly start, DateOnly end) =>
        $"{start.ToString("ddd d MMM", CultureInfo.InvariantCulture)} – {end.ToString("ddd d MMM", CultureInfo.InvariantCulture)}";

    /// <summary>"Day 7 of 10 · 3 working days left", or what stands in for it before
    /// the first working day and once the sprint is over.</summary>
    public static string DayText(SprintProgressDto progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var days = progress.WorkingDays;
        var elapsed = Math.Clamp(progress.WorkingDaysElapsed, 0, days);
        if (!progress.IsOpen) return $"Closed · {Count(days, "working day", "working days")}";
        if (elapsed == 0) return $"Starts {progress.Start.ToString("ddd d MMM", CultureInfo.InvariantCulture)} · {Count(days, "working day", "working days")}";

        var left = days - elapsed;
        return left == 0
            ? $"Day {elapsed} of {days} · the last working day"
            : $"Day {elapsed} of {days} · {Count(left, "working day", "working days")} left";
    }

    /// <summary>The goal rules in words — "+rekeningen, +verplaatsen-specs and tasks
    /// marked goal" — with the share the goal is met at when it is less than every
    /// goal task.</summary>
    public static string GoalRules(IReadOnlyList<GoalRuleDto> rules, int goalShare)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var parts = new List<string>();
        foreach (var rule in rules)
        {
            switch (rule.Kind)
            {
                case GoalRuleKind.Everything:
                    parts.Add("every task in the sprint");
                    break;
                case GoalRuleKind.MarkedAsGoal:
                    parts.Add("tasks marked goal");
                    break;
                case GoalRuleKind.InPlans:
                    parts.AddRange(rule.Choices.Select(Bare).Where(plan => plan.Length > 0).Select(plan => $"+{plan}"));
                    break;
                case GoalRuleKind.Tagged:
                    var tags = rule.Choices.Select(tag => tag.Trim()).Where(tag => tag.Length > 0).ToList();
                    if (tags.Count > 0) parts.Add($"tasks tagged {Join(tags)}");
                    break;
            }
        }

        var text = parts.Count == 0 ? "no goal rule" : Join(parts);
        return goalShare is > 0 and < 100 ? $"{text} · met at {goalShare.ToString(CultureInfo.InvariantCulture)}%" : text;
    }

    /// <summary>
    /// Members done by the end of each working day elapsed so far, cumulative. A member
    /// done before the first day, or done with no day recorded, counts from the first
    /// day; one done after the last day elapsed is not drawn yet.
    /// </summary>
    public static IReadOnlyList<int> DonePerDay(SprintProgressDto progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var days = WorkingDays(progress.Start, progress.End).Take(Math.Max(0, progress.WorkingDaysElapsed)).ToList();
        var done = progress.Members
            .Where(member => member.State is SprintWorkState.Done)
            .Select(member => member.DoneOn ?? progress.Start)
            .ToList();

        return [.. days.Select(day => done.Count(on => on <= day))];
    }

    /// <summary>
    /// One sentence on the sprint against the even pace — every member done by the last
    /// working day, at the same number each day: how many goal tasks are open with how
    /// many working days left, which of them are blocked, and how far all the work is
    /// ahead of or behind that pace.
    /// </summary>
    public static string PaceReading(SprintProgressDto progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var all = progress.AllWork;
        if (all.Total == 0) return progress.IsOpen ? "Nothing is in the sprint yet." : "Nothing was in this sprint.";

        if (!progress.IsOpen)
        {
            return progress.GoalMet
                ? $"Closed with the goal met: {all.Done} of {all.Total} done."
                : $"Closed with the goal not met: {all.Done} of {all.Total} done.";
        }

        var days = Math.Max(1, progress.WorkingDays);
        var elapsed = Math.Clamp(progress.WorkingDaysElapsed, 0, days);
        if (elapsed == 0) return $"The sprint starts on {progress.Start.ToString("dddd d MMMM", CultureInfo.InvariantCulture)} with {Count(all.Total, "task", "tasks")}.";

        var left = days - elapsed;
        var goalOpen = progress.Goal.Total - progress.Goal.Done;
        var goalIds = progress.GoalTaskIds.ToHashSet();
        var blocked = progress.Members
            .Where(member => member.State is SprintWorkState.Blocked && goalIds.Contains(member.TaskId))
            .Select(member => member.Title)
            .ToList();

        var goal = progress.GoalMet
            ? $"Goal met with {Count(left, "working day", "working days")} left"
            : $"Goal: {goalOpen} open with {Count(left, "working day", "working days")} left";
        if (!progress.GoalMet && blocked.Count > 0)
        {
            goal += $"; {Join(blocked)} {(blocked.Count == 1 ? "is" : "are")} blocked";
        }

        var expected = (int)Math.Round((double)all.Total * elapsed / days, MidpointRounding.AwayFromZero);
        var gap = all.Done - expected;
        var pace = gap switch
        {
            0 => "All work is on the even pace.",
            > 0 => $"All work is {gap} ahead of the even pace.",
            _ => $"All work is {-gap} behind the even pace."
        };

        return $"{goal}. {pace}";
    }

    /// <summary>Monday to Friday from <paramref name="start"/> to <paramref name="end"/>,
    /// both inclusive — the days the module counts.</summary>
    public static IEnumerable<DateOnly> WorkingDays(DateOnly start, DateOnly end)
    {
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) yield return day;
        }
    }

    private static SprintBoardRow Row(SprintMemberDto member, bool isGoal) =>
        new(
            member.TaskId.ToString(),
            string.IsNullOrWhiteSpace(member.Title) ? "Untitled" : member.Title,
            member.State switch
            {
                SprintWorkState.Done => SprintRowState.Done,
                SprintWorkState.Blocked => SprintRowState.Blocked,
                SprintWorkState.InProgress => SprintRowState.InProgress,
                _ => SprintRowState.NotStarted
            },
            member.PlanTag,
            member.Repository,
            isGoal,
            member.AddedAfterStart,
            [.. member.PullRequests.Select(pull => new SprintBoardPullRequest(pull.Repository, pull.Number, pull.State switch
            {
                LinkedPullRequestState.Open => SprintPullRequestState.Open,
                LinkedPullRequestState.Draft => SprintPullRequestState.Draft,
                LinkedPullRequestState.Merged => SprintPullRequestState.Merged,
                LinkedPullRequestState.Closed => SprintPullRequestState.Closed,
                _ => SprintPullRequestState.Unknown
            }))],
            member.Owners,
            member.Note,
            member.OpenQuestion);

    private static SprintBoardCounts Counts(SprintCountsDto counts) =>
        new(counts.Done, counts.InProgress, counts.Blocked, counts.NotStarted, counts.AddedAfterStart);

    private static string Count(int count, string one, string many) => SprintBoardRules.Count(count, one, many);

    private static string Join(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => string.Empty,
        1 => parts[0],
        _ => $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}"
    };

    private static string Bare(string tag)
    {
        var trimmed = tag.Trim();
        return trimmed.StartsWith('+') ? trimmed[1..].Trim() : trimmed;
    }
}
