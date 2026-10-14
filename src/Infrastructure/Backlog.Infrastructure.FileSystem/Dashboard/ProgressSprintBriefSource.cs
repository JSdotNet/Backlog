using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.Dashboard;

/// <summary>
/// ADAPTER — answers the dashboard's <see cref="ISprintBriefSource"/> from the Progress
/// context's published surface: its series (<see cref="ISprintSeriesPlanning"/>) and each
/// sprint read against its members' current state (<see cref="ISprintProgress"/>).
/// <para>
/// Only the series switched on and marked "Show it in the weekly brief" are read, and of
/// those only the sprints that overlap the window: a series switched off keeps its choice
/// for when it is switched back on, and draws no Sprint block meanwhile. Each member crosses as a
/// <see cref="SprintBriefMember"/>, because the dashboard may not name a Progress type
/// (guideline ADR 0005).
/// </para>
/// <para>
/// With Progress switched off, or not composed at all, nothing is read and the answer is
/// empty — the brief then draws no Sprint block and is otherwise unchanged. A series or
/// a sprint that cannot be read is left out rather than failing the others.
/// </para>
/// </summary>
public sealed class ProgressSprintBriefSource(
    ISprintSeriesPlanning? planning,
    ISprintProgress? progress,
    IAppFeatureSettings? features) : ISprintBriefSource
{
    public async Task<IReadOnlyList<SprintBriefSprint>> ReadAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (planning is null || progress is null) return [];
        if (features?.IsEnabled(ProgressFeatures.Progress) != true) return [];

        var listed = await planning.ListSeriesAsync(cancellationToken).ConfigureAwait(false);
        if (listed.IsFailure) return [];

        var sprints = new List<SprintBriefSprint>();

        foreach (var series in listed.Value.Where(series => series.IsOn && series.ShowInWeeklyBrief))
        {
            foreach (var sprint in series.Sprints.Where(sprint => sprint.Start <= to && sprint.End >= from))
            {
                var read = await progress.ReadAsync(series.Id, sprint.Number, cancellationToken).ConfigureAwait(false);
                if (read.IsFailure) continue;

                sprints.Add(Map(series, read.Value));
            }
        }

        return sprints;
    }

    private static SprintBriefSprint Map(SprintSeriesDto series, SprintProgressDto sprint)
    {
        var goal = sprint.GoalTaskIds.ToHashSet();

        return new SprintBriefSprint(
            series.Id,
            series.Name,
            series.Repositories,
            sprint.Number,
            sprint.Name,
            sprint.Start,
            sprint.End,
            [.. sprint.Members.Select(member => new SprintBriefMember(
                member.TaskId,
                member.Title,
                member.PlanTag,
                goal.Contains(member.TaskId),
                member.State == SprintWorkState.Blocked,
                member.State == SprintWorkState.Done,
                member.DoneOn,
                member.AddedAfterStart,
                member.Outcome is CloseOutcome.BackToBacklog or CloseOutcome.Drop,
                [.. member.PullRequests.Select(pull => new SprintBriefPullRequest(
                    pull.Repository,
                    pull.Number,
                    pull.State switch
                    {
                        LinkedPullRequestState.Open => SprintBriefPullRequestState.Open,
                        LinkedPullRequestState.Draft => SprintBriefPullRequestState.Draft,
                        LinkedPullRequestState.Merged => SprintBriefPullRequestState.Merged,
                        LinkedPullRequestState.Closed => SprintBriefPullRequestState.Closed,
                        _ => SprintBriefPullRequestState.Unknown
                    },
                    pull.MergedOn))]))]);
    }
}
