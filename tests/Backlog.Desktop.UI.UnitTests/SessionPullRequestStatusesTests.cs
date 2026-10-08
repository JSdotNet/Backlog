using Backlog.Desktop.UI.PullRequests;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the sessions list's pull request card is told about a pull request: where it
/// points on GitHub, and the verdict the pull requests pane would give it.
/// </summary>
public sealed class SessionPullRequestStatusesTests
{
    [Fact]
    public void A_reference_with_a_url_points_at_its_repository_and_number()
    {
        var pin = SessionPullRequestStatuses.PinOf(Reference("PR #1036", "https://github.com/JSdotNet/Backlog/pull/1036", repository: null));

        Assert.Equal(new PullRequestPin("JSdotNet/Backlog", 1036), pin);
    }

    [Fact]
    public void A_reference_with_no_url_is_placed_by_its_repository_and_label()
    {
        var pin = SessionPullRequestStatuses.PinOf(Reference("PR #587", url: null, repository: "JSdotNet/Backlog"));

        Assert.Equal(new PullRequestPin("JSdotNet/Backlog", 587), pin);
    }

    [Theory]
    [InlineData("PR #587", null, "backlog")]
    [InlineData("PR #587", null, null)]
    [InlineData("a pull request", null, "JSdotNet/Backlog")]
    [InlineData("PR #12", "https://example.com/pull/12", null)]
    public void A_reference_that_cannot_be_placed_on_GitHub_is_not_read(string label, string? url, string? repository) =>
        Assert.Null(SessionPullRequestStatuses.PinOf(Reference(label, url, repository)));

    [Fact]
    public void An_open_pull_request_wears_its_verdicts_label_and_tone()
    {
        var status = SessionPullRequestStatuses.Of(Pull() with { Checks = GitHubCheckState.Failing, CheckCounts = new GitHubCheckCounts(3, 2, 0) });

        Assert.Equal("fault", status.Tone);
        Assert.Equal(PullRequestVerdict.Of(Pull() with { Checks = GitHubCheckState.Failing, CheckCounts = new GitHubCheckCounts(3, 2, 0) })!.Label, status.Label);
        Assert.Equal("Add the thing", status.Title);
        Assert.False(string.IsNullOrWhiteSpace(status.Reason));
    }

    [Fact]
    public void A_merged_pull_request_is_merged_and_settled()
    {
        var status = SessionPullRequestStatuses.Of(Pull() with { IsMerged = true });

        Assert.Equal("Merged", status.Label);
        Assert.Equal("settled", status.Tone);
    }

    [Fact]
    public void A_pull_request_closed_without_merging_is_closed_and_archived()
    {
        var status = SessionPullRequestStatuses.Of(Pull() with { IsClosed = true });

        Assert.Equal("Closed", status.Label);
        Assert.Equal("archived", status.Tone);
    }

    private static DeliveryRunReference Reference(string label, string? url, string? repository) =>
        new(DeliveryRunReferenceKind.PullRequest, label, Title: null, url, repository);

    private static GitHubOpenPullRequest Pull() =>
        new(
            Number: 42,
            Url: "https://github.com/acme/app/pull/42",
            Title: "Add the thing",
            RepositoryFullName: "acme/app",
            NodeId: "PR_42",
            IsDraft: false,
            HeadRefName: "feature/thing",
            HeadSha: "abc123",
            BaseRefName: "main",
            AuthorLogin: "octocat",
            ViewerDidAuthor: true,
            Checks: GitHubCheckState.Passing,
            AutoMergeEnabled: false,
            MergeReady: true,
            IsBehind: false,
            HasConflicts: false,
            MergeStateStatus: null,
            PreferredMergeMethod: GitHubMergeMethod.Squash,
            UpdatedAt: DateTimeOffset.Parse("2026-10-08T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture))
        {
            CheckCounts = new GitHubCheckCounts(Passed: 5, Failed: 0, Pending: 0)
        };
}
