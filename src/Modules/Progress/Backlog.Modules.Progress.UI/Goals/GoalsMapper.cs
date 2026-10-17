using System.Globalization;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.UI.Components.Progress;

namespace Backlog.Modules.Progress.UI.Goals;

/// <summary>
/// The Goals view's reading turned into the board the library draws
/// (<c>.devbook/domain/progress/features.md#the-goals-view</c>): the stand-up's goals,
/// counts and forecasts, joined on id with each goal's attachments from
/// <see cref="Abstractions.Services.IGoals.ListAsync"/>, the pull requests' status from
/// <see cref="Abstractions.Services.IGoalPullRequestReader"/>, and the shell's candidates
/// for titles, plan counts, the attach search and No goal. The library takes plain values,
/// so every Progress word is translated here. Pure.
/// </summary>
public static class GoalsMapper
{
    /// <summary>The board over one reading.</summary>
    public static GoalsBoardModel Map(
        StandUpDto standUp,
        IReadOnlyList<GoalSummaryDto> goals,
        IReadOnlyList<GoalPullRequestDto> pullRequests,
        GoalsCandidates candidates)
    {
        ArgumentNullException.ThrowIfNull(standUp);
        ArgumentNullException.ThrowIfNull(goals);
        ArgumentNullException.ThrowIfNull(pullRequests);
        ArgumentNullException.ThrowIfNull(candidates);

        var statuses = new Dictionary<string, GoalPullRequestDto>(StringComparer.Ordinal);
        foreach (var pull in pullRequests)
        {
            if (Identity(pull.Repository, pull.Number) is { } identity) statuses.TryAdd(identity, pull);
        }

        var attachments = goals.ToDictionary(summary => summary.Goal.Id, summary => summary.Goal);
        var tasks = candidates.Tasks.ToDictionary(task => task.Id);

        PullRequestStatusModel Status(string repository, int number, string? title)
        {
            var identity = GoalsKeys.PullRequestIdentity(repository, number);
            var candidate = candidates.PullRequests.FirstOrDefault(pull => GoalsKeys.PullRequestIdentity(pull.Repository, pull.Number) == identity);

            return statuses.TryGetValue(identity, out var read)
                ? new PullRequestStatusModel(
                    number,
                    Slug(read.State),
                    read.Checks is { } checks ? new PullRequestChecks(checks.Passed, checks.Failed, checks.Pending) : null,
                    read.Approvals is { } approvals ? new PullRequestApprovals(approvals.Received, approvals.Required) : null,
                    candidate?.Title ?? title,
                    candidate?.Url ?? Url(repository, number))
                : new PullRequestStatusModel(number, "unknown", Title: candidate?.Title ?? title, Url: candidate?.Url ?? Url(repository, number));
        }

        GoalCardModel Card(StandUpGoalDto goal)
        {
            var attached = attachments.TryGetValue(goal.Id, out var held) ? held.Attachments.Select(a => a.Item).ToList() : [];

            var plans = attached
                .Where(item => item.Kind == GoalAttachmentKind.Plan && !string.IsNullOrWhiteSpace(item.PlanTag))
                .Select(item =>
                {
                    var rows = candidates.Tasks.Where(task => task.PlanTags.Any(tag => GoalsKeys.SamePlan(tag, item.PlanTag))).ToList();
                    return new GoalPlanModel(GoalsKeys.Bare(item.PlanTag), rows.Count(row => row.Status == GoalTaskStatus.Done), rows.Count);
                })
                .ToList();

            var goalTasks = attached
                .Where(item => item.Kind == GoalAttachmentKind.Task && item.TaskId is { } id && tasks.ContainsKey(id))
                .Select(item => tasks[item.TaskId!.Value])
                .ToList();

            var pulls = PullRequestRefs(attached, tasks)
                .Select(pull => Status(pull.Repository, pull.Number, null))
                .ToList();

            return new GoalCardModel(
                goal.Id.ToString(),
                goal.Title,
                goal.DueDate,
                goal.LandsOn,
                goal.Standing == GoalStanding.AtRisk,
                new GoalCounts(goal.Progress.Done, goal.Progress.Total, goal.Progress.Doing, goal.Progress.Blocked, goal.Progress.ToDo, goal.Progress.DoneInWindow),
                plans,
                [.. goalTasks.Select(task => new GoalTaskModel(task.Id.ToString(), task.Title, task.Status))],
                pulls);
        }

        StandUpGroup Group(StandUpGroupDto group) => new(
            group.GoalTitle,
            [.. group.Items.Select(item => new StandUpEntry(
                item.Title,
                item.Kind == GoalAttachmentKind.PullRequest && item.Repository is { } repository && item.Number is { } number
                    ? Status(repository, number, item.Title)
                    : null))]);

        var standUpModel = new StandUpModel(
            [.. standUp.Finished.Select(Group)],
            [.. standUp.Planned.Select(Group)],
            standUp.PlannedEffort,
            standUp.Capacity,
            [.. standUp.Impediments.Select(impediment => new StandUpImpediment(
                impediment.Id.ToString(),
                impediment.Title,
                State(impediment.State),
                [.. ImpedimentMoves.From(impediment.State).Select(State)],
                impediment.With,
                impediment.DaysOpen,
                impediment.CheckAgain,
                impediment.GoalTitle))],
            standUp.ResolvedInLast);

        return new GoalsBoardModel(
            Iteration(standUp.Last.Length),
            new GoalsWindow(standUp.Last.First, standUp.Last.Last),
            new GoalsWindow(standUp.Next.First, standUp.Next.Last),
            [.. standUp.Goals.Select(Card)],
            GoalsHolding.NoGoal(goals.Select(summary => summary.Goal), candidates),
            standUpModel,
            Candidates(candidates));
    }

    /// <summary>Every pull request the board shows, each once, in the order it shows them:
    /// each goal's own and those its tasks link, then the stand-up's.</summary>
    public static IReadOnlyList<PullRequestRefDto> PullRequestsToRead(
        StandUpDto standUp,
        IReadOnlyList<GoalSummaryDto> goals,
        GoalsCandidates candidates)
    {
        ArgumentNullException.ThrowIfNull(standUp);
        ArgumentNullException.ThrowIfNull(goals);
        ArgumentNullException.ThrowIfNull(candidates);

        var attachments = goals.ToDictionary(summary => summary.Goal.Id, summary => summary.Goal);
        var tasks = candidates.Tasks.ToDictionary(task => task.Id);

        var fromGoals = standUp.Goals
            .SelectMany(goal => attachments.TryGetValue(goal.Id, out var held)
                ? PullRequestRefs([.. held.Attachments.Select(a => a.Item)], tasks)
                : []);

        var fromStandUp = standUp.Finished.Concat(standUp.Planned)
            .SelectMany(group => group.Items)
            .Where(item => item.Kind == GoalAttachmentKind.PullRequest && item.Repository is not null && item.Number is not null)
            .Select(item => (Repository: item.Repository!, Number: item.Number!.Value));

        return [.. fromGoals.Concat(fromStandUp)
            .DistinctBy(pull => GoalsKeys.PullRequestIdentity(pull.Repository, pull.Number))
            .Select(pull => new PullRequestRefDto(pull.Repository, pull.Number.ToString(CultureInfo.InvariantCulture)))];
    }

    /// <summary>A length as the switch draws it.</summary>
    public static GoalsIteration Iteration(IterationLength length) => length switch
    {
        IterationLength.Day => GoalsIteration.Day,
        IterationLength.TwoWeeks => GoalsIteration.TwoWeeks,
        IterationLength.Month => GoalsIteration.Month,
        _ => GoalsIteration.Week
    };

    /// <summary>The switch's choice as the module's length.</summary>
    public static IterationLength Length(GoalsIteration iteration) => iteration switch
    {
        GoalsIteration.Day => IterationLength.Day,
        GoalsIteration.TwoWeeks => IterationLength.TwoWeeks,
        GoalsIteration.Month => IterationLength.Month,
        _ => IterationLength.Week
    };

    /// <summary>A state as the picker draws it.</summary>
    public static StandUpImpedimentState State(ImpedimentState state) => state switch
    {
        ImpedimentState.New => StandUpImpedimentState.New,
        ImpedimentState.Reported => StandUpImpedimentState.Reported,
        ImpedimentState.Escalated => StandUpImpedimentState.Escalated,
        ImpedimentState.BeingHandled => StandUpImpedimentState.BeingHandled,
        _ => StandUpImpedimentState.Resolved
    };

    /// <summary>The picker's choice as the module's state.</summary>
    public static ImpedimentState State(StandUpImpedimentState state) => state switch
    {
        StandUpImpedimentState.New => ImpedimentState.New,
        StandUpImpedimentState.Reported => ImpedimentState.Reported,
        StandUpImpedimentState.Escalated => ImpedimentState.Escalated,
        StandUpImpedimentState.BeingHandled => ImpedimentState.BeingHandled,
        _ => ImpedimentState.Resolved
    };

    /// <summary>The attach search's matches: every plan, every task not done, every open
    /// pull request.</summary>
    private static IReadOnlyList<GoalsItem> Candidates(GoalsCandidates candidates) =>
    [
        .. candidates.Plans
            .Select(GoalsKeys.Bare)
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(tag => new GoalsItem(GoalsKeys.ForPlan(tag), GoalsItemKind.Plan, tag)),
        .. candidates.Tasks
            .Where(task => task.Status != GoalTaskStatus.Done)
            .Select(task => new GoalsItem(GoalsKeys.ForTask(task.Id), GoalsItemKind.Task, task.Title)),
        .. candidates.PullRequests
            .Select(pull => new GoalsItem(GoalsKeys.ForPullRequest(pull.Repository, pull.Number), GoalsItemKind.PullRequest, pull.Title, pull.Number))
    ];

    /// <summary>A goal's own pull requests, then those its attached tasks link, each once.</summary>
    private static IEnumerable<(string Repository, int Number)> PullRequestRefs(
        IReadOnlyList<GoalItemDto> attached,
        IReadOnlyDictionary<Guid, GoalsTaskCandidate> tasks)
    {
        var own = attached
            .Where(item => item.Kind == GoalAttachmentKind.PullRequest && item.Repository is not null && item.Number is not null)
            .Select(item => (Repository: item.Repository!, Number: item.Number!.Value));

        var linked = attached
            .Where(item => item.Kind == GoalAttachmentKind.Task && item.TaskId is { } id && tasks.ContainsKey(id))
            .SelectMany(item => tasks[item.TaskId!.Value].PullRequests)
            .Select(pull => (pull.Repository, Number: int.TryParse(pull.Number, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0))
            .Where(pull => pull.Number > 0);

        return own.Concat(linked).DistinctBy(pull => GoalsKeys.PullRequestIdentity(pull.Repository, pull.Number));
    }

    private static string? Identity(string repository, string number) =>
        int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? GoalsKeys.PullRequestIdentity(repository, n) : null;

    private static string Slug(LinkedPullRequestState state) => state.ToString().ToLowerInvariant();

    private static string Url(string repository, int number) =>
        $"https://github.com/{repository}/pull/{number.ToString(CultureInfo.InvariantCulture)}";
}
