using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.UI.Components.Progress;

namespace Backlog.Modules.Progress.UI.Goals;

/// <summary>What the goals hold between them: task ids, and pull requests by
/// <see cref="GoalsKeys.PullRequestIdentity(string,int)"/>.</summary>
public sealed record GoalsHeld(IReadOnlySet<Guid> Tasks, IReadOnlySet<string> PullRequests)
{
    /// <summary>Whether a goal holds the task <paramref name="taskId"/>.</summary>
    public bool HoldsTask(Guid taskId) => Tasks.Contains(taskId);

    /// <summary>Whether a goal holds the pull request <paramref name="number"/> in
    /// <paramref name="repository"/>.</summary>
    public bool HoldsPullRequest(string repository, int number) =>
        PullRequests.Contains(GoalsKeys.PullRequestIdentity(repository, number));
}

/// <summary>
/// What a goal holds, and so what No goal lists
/// (<c>.devbook/domain/progress/requirements.md#requirement-work-in-progress-that-no-goal-holds-is-listed-under-no-goal</c>).
/// A goal holds its attached tasks, the rows filed under its attached plans, its attached
/// pull requests and the pull requests any task it holds links. No goal is every task in
/// progress — blocked or not — and every open pull request under the header's repository
/// scope that no goal holds, tasks first. Pure, over the goals' attachments and the
/// shell's candidates.
/// </summary>
public static class GoalsHolding
{
    /// <summary>What <paramref name="goals"/> hold among <paramref name="tasks"/>.</summary>
    public static GoalsHeld Of(IEnumerable<GoalDto> goals, IReadOnlyList<GoalsTaskCandidate> tasks)
    {
        ArgumentNullException.ThrowIfNull(goals);
        ArgumentNullException.ThrowIfNull(tasks);

        var heldTasks = new HashSet<Guid>();
        var heldPulls = new HashSet<string>(StringComparer.Ordinal);
        var plans = new List<string>();

        foreach (var item in goals.SelectMany(goal => goal.Attachments).Select(attachment => attachment.Item))
        {
            switch (item.Kind)
            {
                case GoalAttachmentKind.Task when item.TaskId is { } id:
                    heldTasks.Add(id);
                    break;
                case GoalAttachmentKind.Plan when !string.IsNullOrWhiteSpace(item.PlanTag):
                    plans.Add(item.PlanTag);
                    break;
                case GoalAttachmentKind.PullRequest when item.Repository is { } repository && item.Number is { } number:
                    heldPulls.Add(GoalsKeys.PullRequestIdentity(repository, number));
                    break;
            }
        }

        foreach (var task in tasks)
        {
            if (task.PlanTags.Any(tag => plans.Any(plan => GoalsKeys.SamePlan(plan, tag)))) heldTasks.Add(task.Id);
        }

        foreach (var task in tasks.Where(task => heldTasks.Contains(task.Id)))
        {
            foreach (var pull in task.PullRequests)
            {
                if (GoalsKeys.PullRequestIdentity(pull) is { } identity) heldPulls.Add(identity);
            }
        }

        return new GoalsHeld(heldTasks, heldPulls);
    }

    /// <summary>The work in progress in scope that none of <paramref name="goals"/> holds,
    /// as rows the board lists under No goal.</summary>
    public static IReadOnlyList<GoalsItem> NoGoal(IEnumerable<GoalDto> goals, GoalsCandidates candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var held = Of(goals, candidates.Tasks);

        var tasks = candidates.Tasks
            .Where(task => task.InScope
                           && task.Status is GoalTaskStatus.Doing or GoalTaskStatus.Blocked
                           && !held.HoldsTask(task.Id))
            .Select(task => new GoalsItem(GoalsKeys.ForTask(task.Id), GoalsItemKind.Task, task.Title));

        var pulls = candidates.PullRequests
            .Where(pull => pull.InScope && !held.HoldsPullRequest(pull.Repository, pull.Number))
            .Select(pull => new GoalsItem(GoalsKeys.ForPullRequest(pull.Repository, pull.Number), GoalsItemKind.PullRequest, pull.Title, pull.Number));

        return [.. tasks, .. pulls];
    }
}
