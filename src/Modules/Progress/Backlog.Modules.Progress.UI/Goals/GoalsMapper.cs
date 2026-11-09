using System.Globalization;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.UI.Components.Goals;
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

        // Told apart as the goal's bar tells them apart, so a task's rows and the bar never
        // read one link differently. The first answer stands, as on the bar.
        var answers = new Dictionary<string, GoalPullRequestDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var pull in pullRequests) answers.TryAdd(GoalItems.Key(pull.Repository, pull.Number), pull);

        // Where a task stands on a goal, by the rule the goal's bar reads (GoalItems), in its
        // order: a pull request with no answer is not merged. The attach search and No goal
        // keep the task's own status, which says nothing of its pull requests.
        GoalTaskStatus Standing(GoalsTaskCandidate task)
        {
            if (task.Status == GoalTaskStatus.Done) return GoalTaskStatus.Done;

            var linked = task.LinksForStanding;
            var merged = linked.Count > 0
                && linked.All(pull => answers.TryGetValue(GoalItems.KeyOf(pull), out var answer) && answer.State == LinkedPullRequestState.Merged);
            if (merged) return GoalTaskStatus.Done;

            return task.Status == GoalTaskStatus.Blocked || task.MarkedBlocked ? GoalTaskStatus.Blocked : task.Status;
        }

        PullRequestStatusModel Status(string repository, int number, string? title)
        {
            var identity = GoalSubjects.PullRequestIdentity(repository, number);
            var candidate = candidates.PullRequests.FirstOrDefault(pull => GoalSubjects.PullRequestIdentity(pull.Repository, pull.Number) == identity);

            return statuses.TryGetValue(identity, out var read)
                ? new PullRequestStatusModel(
                    number,
                    Slug(read.State),
                    read.Checks is { } checks ? new PullRequestChecks(checks.Passed, checks.Failed, checks.Pending) : null,
                    read.Approvals is { } approvals ? new PullRequestApprovals(approvals.Received, approvals.Required) : null,
                    candidate?.Title ?? title,
                    candidate?.Url ?? Url(repository, number),
                    read.ReviewRequested)
                : new PullRequestStatusModel(number, "unknown", Title: candidate?.Title ?? title, Url: candidate?.Url ?? Url(repository, number));
        }

        GoalCardModel Card(StandUpGoalDto goal)
        {
            var stored = attachments.GetValueOrDefault(goal.Id);
            var attached = stored is not null ? stored.Attachments.Select(a => a.Item).ToList() : [];

            var plans = attached
                .Where(IsPlan)
                .Select(item =>
                {
                    var rows = RowsOf(item, candidates).Select(Standing).ToList();
                    return new GoalPlanModel(
                        GoalSubjects.Bare(item.PlanTag),
                        rows.Count(standing => standing == GoalTaskStatus.Done),
                        rows.Count,
                        GoalSubjects.FromItem(item),
                        rows.Count(standing => standing == GoalTaskStatus.Doing),
                        rows.Count(standing => standing == GoalTaskStatus.Blocked));
                })
                .ToList();

            var goalTasks = attached
                .Where(item => item.Kind == GoalAttachmentKind.Task && item.TaskId is { } id && tasks.ContainsKey(id))
                .Select(item => tasks[item.TaskId!.Value])
                .ToList();

            // A goal's own pull request detaches; one only a task brings has no subject.
            var own = attached
                .Where(item => item.Kind == GoalAttachmentKind.PullRequest && item.Repository is not null && item.Number is not null)
                .Select(item => GoalSubjects.PullRequestIdentity(item.Repository!, item.Number!.Value))
                .ToHashSet(StringComparer.Ordinal);
            var refs = PullRequestRefs(attached, tasks).ToList();
            var pulls = refs.Select(pull => Status(pull.Repository, pull.Number, null)).ToList();
            var pullSubjects = refs
                .Select(pull => own.Contains(GoalSubjects.PullRequestIdentity(pull.Repository, pull.Number))
                    ? GoalSubject.ForPullRequest(pull.Repository, pull.Number)
                    : null)
                .ToList();

            return new GoalCardModel(
                goal.Id.ToString(),
                goal.Title,
                goal.DueDate,
                goal.LandsOn,
                goal.Standing == GoalStanding.AtRisk,
                new GoalCounts(goal.Progress.Done, goal.Progress.Total, goal.Progress.Doing, goal.Progress.Blocked, goal.Progress.ToDo, goal.Progress.DoneInWindow),
                plans,
                [.. goalTasks.Select(task => new GoalTaskModel(task.Id.ToString(), task.Title, Standing(task), GoalSubject.ForTask(task.Id)))],
                pulls,
                stored?.Outcome,
                pullSubjects,
                goal.DueIsIterationEnd);
        }

        // A held-back item under the words the reading gives it; a pull request the shell
        // lists takes its own title, since the row draws its number before the title.
        GoalsItem HeldBack(StandUpHeldBackItemDto held)
        {
            var subject = GoalSubjects.FromHeldBack(held.Item);
            if (subject.Kind == GoalSubjectKind.PullRequest)
            {
                var identity = GoalSubjects.PullRequestIdentity(subject.Repository!, subject.Number!.Value);
                var listed = candidates.PullRequests.FirstOrDefault(pull => GoalSubjects.PullRequestIdentity(pull.Repository, pull.Number) == identity);
                return new GoalsItem(subject, listed?.Title ?? subject.Repository!);
            }

            return new GoalsItem(subject, held.Title ?? DeletedTask);
        }

        // A No goal pull request row draws the status line the goals' own draw, from the
        // same reading; a task row already carries its status from GoalsHolding.
        GoalsItem NoGoalRow(GoalsItem row) =>
            row.Subject is { Kind: GoalSubjectKind.PullRequest, Repository: { } repository, Number: { } number }
                ? row with { PullRequest = Status(repository, number, row.Title) }
                : row;

        StandUpGroup Group(StandUpGroupDto group) => new(
            group.GoalTitle,
            [.. group.Items.Select(item => new StandUpEntry(
                item.Title,
                item.Kind == GoalAttachmentKind.PullRequest && item.Repository is { } repository && item.Number is { } number
                    ? Status(repository, number, item.Title)
                    : null,
                item.Effort))]);

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
                impediment.GoalTitle,
                impediment.Note,
                impediment.ReadyToResolve,
                [.. impediment.HoldsBack.Select(HeldBack)]))],
            standUp.ResolvedInLast);

        return new GoalsBoardModel(
            Iteration(standUp.Last.Length),
            new GoalsWindow(standUp.Last.First, standUp.Last.Last),
            new GoalsWindow(standUp.Next.First, standUp.Next.Last),
            [.. standUp.Goals.Select(Card)],
            [.. GoalsHolding.NoGoal(goals.Select(summary => summary.Goal), candidates).Select(NoGoalRow)],
            standUpModel,
            Candidates(candidates))
        {
            Repositories = candidates.Repositories
        };
    }

    /// <summary>The words a held-back task the reading does not have — deleted — reads under.</summary>
    public const string DeletedTask = "A task no longer in the backlog";

    /// <summary>Every pull request the board reads, each once: those it shows, in the order it
    /// shows them — each goal's own and those its tasks link, then the stand-up's, then No
    /// goal's — and last every link of its attached tasks and plans' rows, which it need not
    /// show and needs to say where each of those tasks stands.</summary>
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

        // No goal's rows draw a status line too; the open read has usually filled the shared
        // cache for them already, so this asks for nothing new in the common case.
        var fromNoGoal = GoalsHolding.NoGoal(goals.Select(summary => summary.Goal), candidates)
            .Where(row => row.Subject is { Kind: GoalSubjectKind.PullRequest, Repository: not null, Number: not null })
            .Select(row => (Repository: row.Subject.Repository!, Number: row.Subject.Number!.Value));

        // The stand-up asked for these same pull requests just before this read, through the
        // same shared cache, so they normally ask GitHub for nothing new.
        IEnumerable<GoalsTaskCandidate> TasksStanding(GoalItemDto item)
        {
            if (IsPlan(item)) return RowsOf(item, candidates);
            return item.Kind == GoalAttachmentKind.Task && item.TaskId is { } id && tasks.TryGetValue(id, out var task) ? [task] : [];
        }

        var fromStanding = standUp.Goals
            .SelectMany(goal => attachments.TryGetValue(goal.Id, out var held) ? held.Attachments.Select(a => a.Item) : [])
            .SelectMany(TasksStanding)
            .SelectMany(task => task.LinksForStanding)
            .Select(pull => (Repository: pull.Repository.Trim(), Number: int.TryParse(pull.Number.Trim().TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0))
            .Where(pull => pull.Number > 0);

        return [.. fromGoals.Concat(fromStandUp).Concat(fromNoGoal).Concat(fromStanding)
            .DistinctBy(pull => GoalSubjects.PullRequestIdentity(pull.Repository, pull.Number))
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
            .Select(GoalSubjects.Bare)
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(tag => new GoalsItem(GoalSubject.ForPlan(tag), tag)),
        .. candidates.Tasks
            .Where(task => task.Status != GoalTaskStatus.Done)
            .Select(task => new GoalsItem(GoalSubject.ForTask(task.Id), task.Title)),
        .. candidates.PullRequests
            .Select(pull => new GoalsItem(GoalSubject.ForPullRequest(pull.Repository, pull.Number), pull.Title))
    ];

    private static bool IsPlan(GoalItemDto item) =>
        item.Kind == GoalAttachmentKind.Plan && !string.IsNullOrWhiteSpace(item.PlanTag);

    /// <summary>The shell's tasks filed under an attached plan.</summary>
    private static IEnumerable<GoalsTaskCandidate> RowsOf(GoalItemDto plan, GoalsCandidates candidates) =>
        candidates.Tasks.Where(task => task.PlanTags.Any(tag => GoalSubjects.SamePlan(tag, plan.PlanTag)));

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

        return own.Concat(linked).DistinctBy(pull => GoalSubjects.PullRequestIdentity(pull.Repository, pull.Number));
    }

    private static string? Identity(string repository, string number) =>
        int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? GoalSubjects.PullRequestIdentity(repository, n) : null;

    private static string Slug(LinkedPullRequestState state) => state.ToString().ToLowerInvariant();

    private static string Url(string repository, int number) =>
        $"https://github.com/{repository}/pull/{number.ToString(CultureInfo.InvariantCulture)}";
}
