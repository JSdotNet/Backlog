using Backlog.Infrastructure.Sessions;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// What a review link in a run's Personal Validation question is to the person reading it on
/// another device: the pull request, shown first; an address only the desktop that runs the
/// session can open, marked so; or a page anyone can open
/// (<c>.devbook/domain/notifications/requirements.md#requirement-a-run-at-personal-validation-asks-to-be-approved</c>).
/// </summary>
public sealed class ReviewLinksTests
{
    [Theory]
    [InlineData("http://localhost:5000")]
    [InlineData("https://LOCALHOST:7443/storybook/")]
    [InlineData("http://localhost")]
    [InlineData("http://127.0.0.1:61234/tasks")]
    [InlineData("http://[::1]:5000/")]
    [InlineData("http://storybook.localhost:6006")]
    [InlineData("http://Desktop.LocalHost/")]
    [InlineData("file:///D:/Repos/Backlog/.qa-workspace/toast.png")]
    [InlineData("FILE:///tmp/evidence.png")]
    public void An_address_only_this_machine_can_open_is_desktop_only(string url) =>
        Assert.Equal(ReviewLinkKind.DesktopOnly, ReviewLinks.Classify(url));

    [Theory]
    [InlineData("https://github.com/JSdotNet/Backlog/pull/1652")]
    [InlineData("https://github.com/jsdotnet/backlog/pull/1652/files")]
    [InlineData("https://www.github.com/JSdotNet/Backlog/pull/7?w=1")]
    [InlineData("http://GITHUB.com/JSdotNet/Backlog/pull/7#discussion_r1")]
    public void A_github_pull_request_address_is_the_pull_request(string url) =>
        Assert.Equal(ReviewLinkKind.PullRequest, ReviewLinks.Classify(url));

    [Theory]
    [InlineData("https://localhost.example.com/review")]
    [InlineData("https://notlocalhost:5000")]
    [InlineData("https://github.com/JSdotNet/Backlog/issues/1652")]
    [InlineData("https://github.com/JSdotNet/Backlog/pull/")]
    [InlineData("https://github.com.evil.example/JSdotNet/Backlog/pull/1")]
    [InlineData("https://backlog.example.com/runs/42")]
    [InlineData(".qa-workspace/toast.png")]
    public void Any_other_address_is_a_page_anyone_can_open(string url) =>
        Assert.Equal(ReviewLinkKind.Web, ReviewLinks.Classify(url));

    [Fact]
    public void A_pull_request_address_names_its_repository_and_number()
    {
        Assert.Equal(("JSdotNet/Backlog", 1652), ReviewLinks.PullRequestOf("https://github.com/JSdotNet/Backlog/pull/1652/files"));
        Assert.Null(ReviewLinks.PullRequestOf("http://localhost:5000"));
    }
}
