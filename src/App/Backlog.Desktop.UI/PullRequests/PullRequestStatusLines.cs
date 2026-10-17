using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.Progress;

namespace Backlog.Desktop.UI.PullRequests;

/// <summary>
/// An open pull request as its status line draws it
/// (<c>.devbook/domain/progress/features.md#pull-request-status</c>): checks as
/// <c>passed/total</c> from the head commit's counts, approvals as
/// <c>received/required</c> against what its base branch's rules ask.
/// <para>
/// One home for the desktop's open read, so the Pull requests pane's row and a task's
/// work-link card draw the same marks for the same pull request. The required total is
/// a verdict only where the rules were read
/// (<see cref="GitHubReviewSummary.RequiredRead"/>); otherwise the received count stands
/// alone with no verdict.
/// </para>
/// </summary>
public static class PullRequestStatusLines
{
    /// <summary>The status line of a pull request the open list or the pins read.</summary>
    public static PullRequestStatusModel Of(GitHubOpenPullRequest pull)
    {
        ArgumentNullException.ThrowIfNull(pull);

        return new PullRequestStatusModel(
            pull.Number,
            pull.State switch
            {
                GitHubItemState.Merged => "merged",
                GitHubItemState.Closed => "closed",
                GitHubItemState.Draft => "draft",
                _ => "open"
            },
            pull.CheckCounts is { } counts ? new PullRequestChecks(counts.Passed, counts.Failed, counts.Pending) : null,
            new PullRequestApprovals(pull.Reviews.Approvals, pull.Reviews.Required, pull.Reviews.RequiredRead),
            pull.Title,
            pull.Url);
    }
}
