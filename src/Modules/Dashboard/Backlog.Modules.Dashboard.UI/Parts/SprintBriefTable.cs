using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.UI.Components.Progress;

namespace Backlog.Modules.Dashboard.UI.Parts;

/// <summary>
/// The table the Sprint block's Copy as table puts on the clipboard
/// (<c>.devbook/domain/progress/features.md#sprints-in-the-briefs</c>): the sprint view's
/// own <see cref="SprintTableClipboard"/> table, built from the block's sprint as the
/// dashboard's port answered it — every member, whatever week the brief shows, grouped
/// by plan with the goal plans first. Pure, so its tests need no Progress behind them.
/// </summary>
public static class SprintBriefTable
{
    /// <summary>The table over every member of <paramref name="sprint"/>.</summary>
    public static SprintTable Build(SprintBriefSprint sprint)
    {
        ArgumentNullException.ThrowIfNull(sprint);
        return SprintTableClipboard.Build([.. sprint.Members.Select(Row)], sprint.GoalPlans);
    }

    /// <summary>One member as the sprint view's board draws it, from what the port
    /// carries: the same state, plan, pull requests, owners and note.</summary>
    public static SprintBoardRow Row(SprintBriefMember member)
    {
        ArgumentNullException.ThrowIfNull(member);

        return new SprintBoardRow(
            member.TaskId.ToString(),
            string.IsNullOrWhiteSpace(member.Title) ? "Untitled" : member.Title,
            member switch
            {
                { IsDone: true } => SprintRowState.Done,
                { IsBlocked: true } => SprintRowState.Blocked,
                { IsInProgress: true } => SprintRowState.InProgress,
                _ => SprintRowState.NotStarted
            },
            member.PlanTag,
            member.Repository,
            member.IsGoal,
            member.AddedAfterStart,
            [.. member.PullRequests.Select(pull => new SprintBoardPullRequest(pull.Repository, pull.Number, pull.State switch
            {
                SprintBriefPullRequestState.Open => SprintPullRequestState.Open,
                SprintBriefPullRequestState.Draft => SprintPullRequestState.Draft,
                SprintBriefPullRequestState.Merged => SprintPullRequestState.Merged,
                SprintBriefPullRequestState.Closed => SprintPullRequestState.Closed,
                _ => SprintPullRequestState.Unknown
            }))],
            member.Owners,
            member.Note,
            member.OpenQuestion);
    }
}
