using Backlog.Desktop.UI.PullRequests;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the sessions list's pull request card is told about a pull request: where it
/// points on GitHub, and Repository Management's one verdict on it — the label and tone
/// the pull requests pane gives the same status (local ADR 0042).
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

    /// <summary>One status, one verdict: the card wears the label and the tone
    /// <see cref="PullRequestVerdict.Of(PullRequestStatus)"/> gives the status, the same
    /// the pane's row wears for it.</summary>
    [Fact]
    public void An_open_pull_request_wears_its_verdicts_label_and_tone()
    {
        var pull = Pull() with { Checks = PullRequestCheckState.Failing, CheckCounts = new PullRequestCheckCounts(3, 2, 0) };
        var verdict = PullRequestVerdict.Of(pull)!;

        var status = SessionPullRequestStatuses.Of(pull);

        Assert.Equal(verdict.Label, status.Label);
        Assert.Equal(verdict.Tone.ToString().ToLowerInvariant(), status.Tone);
        Assert.Equal(("2 failing", "fault"), (status.Label, status.Tone));
        Assert.Equal("Add the thing", status.Title);
        Assert.Equal(verdict.Reason, status.Reason);
    }

    /// <summary>A stacked pull request's card names the pull request it waits on, as
    /// the pane's banner does, so the two screens give it the same reason.</summary>
    [Fact]
    public void A_stacked_pull_request_names_its_parent_as_the_pane_does()
    {
        var pull = Pull() with { BaseRefName = "feature/base", StackParent = new PullRequestReference("acme/app", 41) };

        var status = SessionPullRequestStatuses.Of(pull);

        Assert.Equal(PullRequestVerdict.Of(pull)!.Reason, status.Reason);
        Assert.EndsWith("and it waits on #41, which has to merge first.", status.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_merged_pull_request_is_merged_and_settled()
    {
        var status = SessionPullRequestStatuses.Of(Pull() with { State = PullRequestState.Merged });

        Assert.Equal("Merged", status.Label);
        Assert.Equal("settled", status.Tone);
    }

    [Fact]
    public void A_pull_request_closed_without_merging_is_closed_and_archived()
    {
        var status = SessionPullRequestStatuses.Of(Pull() with { State = PullRequestState.Closed });

        Assert.Equal("Closed", status.Label);
        Assert.Equal("archived", status.Tone);
    }

    private static DeliveryRunReference Reference(string label, string? url, string? repository) =>
        new(DeliveryRunReferenceKind.PullRequest, label, Title: null, url, repository);

    private static PullRequestStatus Pull() =>
        new(
            RepositoryFullName: "acme/app",
            Number: 42,
            Url: "https://github.com/acme/app/pull/42",
            Title: "Add the thing",
            State: PullRequestState.Open,
            HeadRefName: "feature/thing",
            BaseRefName: "main",
            AuthorLogin: "octocat",
            ViewerDidAuthor: true,
            UpdatedAt: DateTimeOffset.Parse("2026-10-08T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture))
        {
            Checks = PullRequestCheckState.Passing,
            MergeReady = true,
            CheckCounts = new PullRequestCheckCounts(Passed: 5, Failed: 0, Pending: 0)
        };
}
