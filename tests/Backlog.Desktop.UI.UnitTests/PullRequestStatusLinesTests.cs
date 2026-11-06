using Backlog.Desktop.UI.PullRequests;
using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The status line the Pull requests pane's row and a task's work-link card draw from
/// Repository Management's status: the same "your review" a goal shows, so a pull request
/// reads the same on a goal as in the pane.
/// </summary>
public sealed class PullRequestStatusLinesTests
{
    [Theory]
    [InlineData(PullRequestState.Open, true, true)]
    [InlineData(PullRequestState.Draft, true, true)]
    [InlineData(PullRequestState.Open, false, false)]
    [InlineData(PullRequestState.Merged, true, false)]
    public void The_line_says_your_review_where_an_open_pull_request_asks_for_the_viewers_review(
        PullRequestState state, bool requested, bool shows)
    {
        var line = PullRequestStatusLines.Of(Pull(state) with { ViewerReviewRequested = requested });

        Assert.Equal(shows, line.ShowsYourReview);
    }

    private static PullRequestStatus Pull(PullRequestState state) =>
        new("JSdotNet/Backlog", 1341, "https://github.com/JSdotNet/Backlog/pull/1341", "Invoice PDF export", state,
            "feature/invoice", "main", "octo", false, null);
}
