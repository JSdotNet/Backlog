using System.Net;

using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// Closing an issue as completed — what finishing a linked task at its source is on
/// GitHub (local ADR 0020, Consequences §8). One REST call, through the repository's
/// own issues path, so it goes out as the account the repository is bound to.
/// </summary>
public sealed class GitHubClientCloseIssueTests
{
    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    [Fact]
    public async Task The_issue_is_patched_closed_as_completed()
    {
        var transport = new RoutingTransport().Returns(HttpMethod.Patch, "issues/412", """{ "number": 412, "state": "closed" }""");

        await new GitHubClient(transport).CloseIssueAsync(Repository, 412, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Patch, Assert.Single(transport.Methods));
        Assert.Equal("repos/JSdotNet/Backlog/issues/412", Assert.Single(transport.Paths));
        Assert.Equal("""{"state":"closed","state_reason":"completed"}""", Assert.Single(transport.Bodies));
    }

    /// <summary>The path's <c>repos/owner/name</c> is what the settings read the
    /// account from, so a repository bound to a work account is closed as that
    /// account.</summary>
    [Fact]
    public async Task The_close_routes_to_the_repositorys_account()
    {
        var transport = new RoutingTransport();
        await new GitHubClient(transport).CloseIssueAsync(Repository, 412, TestContext.Current.CancellationToken);
        var settings = new GitHubSettings
        {
            Repositories = [Repository with { Account = "work-login" }],
            Accounts = [new GitHubAccount("work-login") { Credential = GitHubCredentialKind.PersonalAccessToken, Token = "t" }],
        };

        var choice = settings.AccountForPath(Assert.Single(transport.Paths));

        Assert.True(choice.IsBound);
        Assert.Equal("work-login", choice.Login);
    }

    [Fact]
    public async Task A_refusal_reaches_the_caller_with_its_status()
    {
        var transport = new RoutingTransport().Refuses(HttpMethod.Patch, "issues/412", HttpStatusCode.Forbidden, "Resource not accessible by personal access token");

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            new GitHubClient(transport).CloseIssueAsync(Repository, 412, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
    }
}
