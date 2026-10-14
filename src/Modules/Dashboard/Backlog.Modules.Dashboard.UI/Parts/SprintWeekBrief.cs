using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.UI.Parts;

/// <summary>One done member of the reported week, with the pull requests it merged.</summary>
public sealed record SprintShippedItem(string Title, IReadOnlyList<SprintBriefPullRequest> Merged);

/// <summary>The members done in the reported week that are filed under one plan, or
/// under none when <paramref name="PlanTag"/> is null.</summary>
public sealed record SprintShippedPlan(string? PlanTag, IReadOnlyList<SprintShippedItem> Items);

/// <summary>An open pull request of an unfinished member: waiting on review.</summary>
public sealed record SprintWaitingOnReview(string Title, SprintBriefPullRequest PullRequest);

/// <summary>
/// One sprint as the weekly brief's Sprint block reports it for one week: the figures,
/// worked out from the sprint's members as of the week's end.
/// </summary>
/// <remarks>
/// <para>
/// A derivation over what was read, on <see cref="BriefWeek.Read"/>'s pattern: the brief
/// reads every sprint its weeks can reach once, and stepping a week back moves what the
/// block draws and never what it fetches. "Done" is as of the earlier of the week's last
/// day and the sprint's — a member done later was not done then. Blocked, added and
/// removed are the members' current marks: the sprint keeps no history of them.
/// </para>
/// <para>
/// The even pace is the share of the sprint's items that would be done by then if they
/// were done at the same rate every working day, Monday to Friday, rounded to a whole
/// item.
/// </para>
/// </remarks>
/// <param name="Week">Which week of the sprint the reported week is, from one.</param>
/// <param name="Weeks">How many weeks the sprint spans.</param>
/// <param name="Blocked">The titles of the members blocked and not done.</param>
/// <param name="Merged">Distinct pull requests that merged in the reported week.</param>
/// <param name="Open">Members not done by the week's end.</param>
public sealed record SprintWeekBrief(
    SprintBriefSprint Sprint,
    int Week,
    int Weeks,
    int GoalDone,
    int GoalTotal,
    int Done,
    int Total,
    int EvenPace,
    int Added,
    int Removed,
    IReadOnlyList<string> Blocked,
    int DoneThisWeek,
    int Merged,
    IReadOnlyList<SprintShippedPlan> Shipped,
    int Open,
    IReadOnlyList<SprintWaitingOnReview> WaitingOnReview)
{
    /// <summary>Whether the reported week is the sprint's last.</summary>
    public bool IsLastWeek => Week >= Weeks;

    /// <summary>
    /// The block's sprints for <paramref name="week"/>: per series, the latest of its
    /// sprints that overlaps the week, kept when the series has a repository in
    /// <paramref name="focus"/>. In series-name order.
    /// </summary>
    public static IReadOnlyList<SprintWeekBrief> For(
        IEnumerable<SprintBriefSprint> sprints,
        BriefWeek week,
        RepositoryFocus focus)
    {
        ArgumentNullException.ThrowIfNull(sprints);
        ArgumentNullException.ThrowIfNull(week);
        ArgumentNullException.ThrowIfNull(focus);

        return [.. sprints
            .Where(sprint => sprint.Start <= week.Sunday && sprint.End >= week.Monday)
            .Where(sprint => focus.IsAll || sprint.Repositories.Any(focus.Contains))
            .GroupBy(sprint => sprint.SeriesId)
            .Select(series => series.MaxBy(sprint => sprint.Number)!)
            .OrderBy(sprint => sprint.SeriesName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(sprint => sprint.SeriesId)
            .Select(sprint => Of(sprint, week))];
    }

    /// <summary><paramref name="sprint"/> read for <paramref name="week"/>.</summary>
    public static SprintWeekBrief Of(SprintBriefSprint sprint, BriefWeek week)
    {
        ArgumentNullException.ThrowIfNull(sprint);
        ArgumentNullException.ThrowIfNull(week);

        var cutoff = week.Sunday < sprint.End ? week.Sunday : sprint.End;
        var weeks = Math.Max(1, (sprint.End.DayNumber - sprint.Start.DayNumber + 7) / 7);
        var n = Math.Clamp((cutoff.DayNumber - sprint.Start.DayNumber) / 7 + 1, 1, weeks);

        bool DoneBy(SprintBriefMember member) =>
            member.IsDone && (member.DoneOn is not { } on || on <= cutoff);

        bool InWeek(DateOnly? day) =>
            day is { } on && on >= week.Monday && on <= week.Sunday;

        var members = sprint.Members.Where(member => !member.Removed).ToList();
        var goal = members.Where(member => member.IsGoal).ToList();
        var done = members.Count(DoneBy);

        var workingDays = WorkingDays(sprint.Start, sprint.End);
        var elapsed = WorkingDays(sprint.Start, cutoff);
        var pace = workingDays == 0
            ? members.Count
            : (int)Math.Round(members.Count * (decimal)elapsed / workingDays, MidpointRounding.AwayFromZero);

        var shippedThisWeek = members.Where(member => member.IsDone && InWeek(member.DoneOn)).ToList();

        var shipped = shippedThisWeek
            .GroupBy(member => string.IsNullOrWhiteSpace(member.PlanTag) ? null : member.PlanTag.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(plan => plan.Key is null ? 1 : 0)
            .ThenBy(plan => plan.Key, StringComparer.OrdinalIgnoreCase)
            .Select(plan => new SprintShippedPlan(
                plan.Key,
                [.. plan.Select(member => new SprintShippedItem(
                    member.Title,
                    [.. member.PullRequests.Where(pull => pull.State == SprintBriefPullRequestState.Merged)]))]))
            .ToList();

        var merged = sprint.Members
            .SelectMany(member => member.PullRequests)
            .Where(pull => pull.State == SprintBriefPullRequestState.Merged && InWeek(pull.MergedOn))
            .Select(pull => (Repository: pull.Repository.Trim().ToUpperInvariant(), Number: pull.Number.Trim()))
            .Distinct()
            .Count();

        var unfinished = members.Where(member => !DoneBy(member)).ToList();

        return new SprintWeekBrief(
            sprint,
            n,
            weeks,
            goal.Count(DoneBy),
            goal.Count,
            done,
            members.Count,
            pace,
            sprint.Members.Count(member => member.AddedAfterStart),
            sprint.Members.Count(member => member.Removed),
            [.. unfinished.Where(member => member.IsBlocked).Select(member => member.Title)],
            shippedThisWeek.Count,
            merged,
            shipped,
            unfinished.Count,
            [.. unfinished.SelectMany(member => member.PullRequests
                .Where(pull => pull.State == SprintBriefPullRequestState.Open)
                .Select(pull => new SprintWaitingOnReview(member.Title, pull)))]);
    }

    /// <summary>A pull request's address on GitHub, or null when its repository is not
    /// <c>owner/name</c> or its number is not a number — drawn then as its number alone
    /// rather than as a link to nowhere.</summary>
    public static string? Url(SprintBriefPullRequest pullRequest)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);

        var repository = pullRequest.Repository.Trim();
        var parts = repository.Split('/');

        return parts.Length == 2 && parts.All(part => part.Length > 0)
            && int.TryParse(pullRequest.Number.Trim(), out var number) && number > 0
                ? $"https://github.com/{repository}/pull/{number}"
                : null;
    }

    /// <summary>Monday to Friday from <paramref name="from"/> to <paramref name="to"/>,
    /// both inclusive; zero when <paramref name="to"/> is earlier.</summary>
    internal static int WorkingDays(DateOnly from, DateOnly to)
    {
        var count = 0;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) count++;
        }

        return count;
    }
}
