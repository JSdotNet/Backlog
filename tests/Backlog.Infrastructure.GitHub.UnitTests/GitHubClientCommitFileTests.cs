using System.Net;
using System.Text;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// Writing a file that is written again and again: the blob being replaced has
/// to be named, so the client asks for it first; and a file GitHub already
/// holds byte for byte is recognised from that one answer and left alone.
/// </summary>
public sealed class GitHubClientCommitFileTests
{
    private static readonly GitHubRepositoryRef Repository = new("notes", "JSdotNet", "Notes");

    private static readonly byte[] Content = Encoding.ASCII.GetBytes("hello backlog");

    [Fact]
    public async Task A_file_that_is_not_there_yet_is_created_without_a_sha()
    {
        var transport = new RoutingTransport()
            .Refuses(HttpMethod.Get, "contents/backlog/backlog.db", HttpStatusCode.NotFound, "Not Found")
            .Returns(HttpMethod.Put, "contents/backlog/backlog.db", """{ "content": { "sha": "0123abcd" } }""");
        var client = new GitHubClient(transport);

        var committed = await client.CommitFileAsync(Repository, "backlog/backlog.db", Content, "Back up", TestContext.Current.CancellationToken);

        Assert.True(committed.Committed);
        Assert.Equal("0123abcd", committed.Sha);
        Assert.Equal("backlog/backlog.db", committed.Path);

        var put = transport.Bodies[1];
        Assert.NotNull(put);
        Assert.DoesNotContain("\"sha\"", put);
        Assert.Contains(Convert.ToBase64String(Content), put);
    }

    [Fact]
    public async Task A_file_that_changed_is_replaced_naming_the_blob_it_replaces()
    {
        var transport = new RoutingTransport()
            .Returns(HttpMethod.Get, "contents/backlog/backlog.db", """{ "sha": "olderblob" }""")
            .Returns(HttpMethod.Put, "contents/backlog/backlog.db", """{ "content": { "sha": "newerblob" } }""");
        var client = new GitHubClient(transport);

        var committed = await client.CommitFileAsync(Repository, "backlog/backlog.db", Content, "Back up", TestContext.Current.CancellationToken);

        Assert.True(committed.Committed);
        Assert.Contains("\"sha\":\"olderblob\"", transport.Bodies[1]);
    }

    /// <summary>The blob id GitHub reports is git's own — SHA-1 over a header and
    /// the bytes — so the client can tell "unchanged" from one GET, and does.</summary>
    [Fact]
    public async Task A_file_github_already_holds_byte_for_byte_makes_no_commit()
    {
        var sha = GitHubClient.GitBlobSha(Content);
        var transport = new RoutingTransport()
            .Returns(HttpMethod.Get, "contents/backlog/backlog.db", $$"""{ "sha": "{{sha}}" }""")
            .Returns(HttpMethod.Put, "contents/backlog/backlog.db", """{ "content": { "sha": "should-not-be-asked" } }""");
        var client = new GitHubClient(transport);

        var committed = await client.CommitFileAsync(Repository, "backlog/backlog.db", Content, "Back up", TestContext.Current.CancellationToken);

        Assert.False(committed.Committed);
        Assert.Equal(sha, committed.Sha);
        Assert.Single(transport.Paths);
    }

    /// <summary>Pinned against git itself: <c>printf "hello world" | git hash-object --stdin</c>.</summary>
    [Fact]
    public void The_blob_sha_is_the_one_git_would_give()
    {
        Assert.Equal("95d09f2b10159347eece71399a7e2e907ea3df4f", GitHubClient.GitBlobSha(Encoding.ASCII.GetBytes("hello world")));
    }

    /// <summary>Segment by segment, so the folder stays a folder rather than
    /// becoming one file with a slash in its name.</summary>
    [Fact]
    public async Task A_path_with_folders_is_sent_as_a_path_with_folders()
    {
        var transport = new RoutingTransport()
            .Refuses(HttpMethod.Get, "contents/", HttpStatusCode.NotFound)
            .Returns(HttpMethod.Put, "contents/", """{ "content": { "sha": "x" } }""");
        var client = new GitHubClient(transport);

        await client.CommitFileAsync(Repository, "backlog/backlog.db", Content, "Back up", TestContext.Current.CancellationToken);

        Assert.All(transport.Paths, path => Assert.EndsWith("contents/backlog/backlog.db", path));
    }

    /// <summary>A not-found on the write, after the lookup already answered
    /// not-found, is the repository rather than the file — and is said in the
    /// same words whichever transport answered.</summary>
    [Fact]
    public async Task A_repository_that_is_not_there_is_named_as_such()
    {
        var transport = new RoutingTransport()
            .Refuses(HttpMethod.Get, "contents/", HttpStatusCode.NotFound, "gh: Not Found (HTTP 404)")
            .Refuses(HttpMethod.Put, "contents/", HttpStatusCode.NotFound, "gh: Not Found (HTTP 404)");
        var client = new GitHubClient(transport);

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            client.CommitFileAsync(Repository, "backlog/backlog.db", Content, "Back up", TestContext.Current.CancellationToken));

        Assert.Contains("couldn't find that repository", refused.Message);
        Assert.True(refused.IsNotFound);
    }

    /// <summary>A refusal that is not "not found" — a token without the scope,
    /// a repository that is not there — is the caller's to hear, in GitHub's words.</summary>
    [Fact]
    public async Task Any_other_refusal_on_the_lookup_is_reported()
    {
        var transport = new RoutingTransport()
            .Refuses(HttpMethod.Get, "contents/", HttpStatusCode.Forbidden, "GitHub refused the request — the token may lack repo scope.");
        var client = new GitHubClient(transport);

        var refused = await Assert.ThrowsAsync<GitHubException>(() =>
            client.CommitFileAsync(Repository, "backlog/backlog.db", Content, "Back up", TestContext.Current.CancellationToken));

        Assert.Contains("repo scope", refused.Message);
        Assert.Single(transport.Paths);
    }
}
