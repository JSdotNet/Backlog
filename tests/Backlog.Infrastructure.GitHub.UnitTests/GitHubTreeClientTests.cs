using System.Text;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The two calls lazy branch loading is made of, against the JSON GitHub's
/// tree and blob endpoints actually return.
/// </summary>
public class GitHubTreeClientTests
{
    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_listing_is_the_recursive_tree_of_the_commit()
    {
        var transport = new RoutingTransport().Returns(
            "git/trees/abc123",
            """
            {
              "sha": "abc123",
              "truncated": false,
              "tree": [
                { "path": ".arc42", "mode": "040000", "type": "tree", "sha": "t1" },
                { "path": ".arc42/01-intro.md", "mode": "100644", "type": "blob", "sha": "b1", "size": 42 },
                { "path": "vendor", "mode": "160000", "type": "commit", "sha": "c1" }
              ]
            }
            """);

        var listing = await new GitHubTreeClient(transport).ListTreeAsync(Repository, "abc123", Token);

        Assert.Equal("repos/JSdotNet/Backlog/git/trees/abc123?recursive=1", Assert.Single(transport.Paths));
        Assert.False(listing.Truncated);
        Assert.Equal(2, listing.Entries.Count);
        Assert.Equal(new GitHubTreeEntry(".arc42", "t1", true, null), listing.Entries[0]);
        Assert.Equal(new GitHubTreeEntry(".arc42/01-intro.md", "b1", false, 42), listing.Entries[1]);
    }

    /// <summary>A submodule is a pointer to another repository, not a file this
    /// one can serve, so it is left out rather than listed as either kind.</summary>
    [Fact]
    public async Task A_submodule_is_not_listed()
    {
        var transport = new RoutingTransport().Returns(
            "git/trees",
            """{ "tree": [ { "path": "vendor", "type": "commit", "sha": "c1" } ] }""");

        var listing = await new GitHubTreeClient(transport).ListTreeAsync(Repository, "abc123", Token);

        Assert.Empty(listing.Entries);
    }

    [Fact]
    public async Task A_truncated_listing_says_so_rather_than_failing()
    {
        var transport = new RoutingTransport().Returns(
            "git/trees",
            """{ "truncated": true, "tree": [ { "path": "a.md", "type": "blob", "sha": "b1" } ] }""");

        var listing = await new GitHubTreeClient(transport).ListTreeAsync(Repository, "abc123", Token);

        Assert.True(listing.Truncated);
        Assert.Single(listing.Entries);
    }

    [Fact]
    public async Task A_response_with_no_tree_is_a_refusal_with_a_reason()
    {
        var transport = new RoutingTransport().Returns("git/trees", """{ "message": "Not Found" }""");

        var exception = await Assert.ThrowsAsync<GitHubException>(() =>
            new GitHubTreeClient(transport).ListTreeAsync(Repository, "abc123", Token));

        Assert.Contains("did not return a tree", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>GitHub base64-encodes blob content with a line break every sixty
    /// characters, and the decoder has to take that as it comes.</summary>
    [Fact]
    public async Task A_blob_is_decoded_from_github_s_wrapped_base64()
    {
        var text = "# A chapter long enough that the encoding wraps onto more than one line of base64.";
        var wrapped = string.Join("\\n", Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).Chunk(60).Select(chunk => new string(chunk)));
        var transport = new RoutingTransport().Returns(
            "git/blobs/b1",
            $$"""{ "sha": "b1", "encoding": "base64", "content": "{{wrapped}}\n" }""");

        var bytes = await new GitHubTreeClient(transport).ReadBlobAsync(Repository, "b1", Token);

        Assert.Equal("repos/JSdotNet/Backlog/git/blobs/b1", Assert.Single(transport.Paths));
        Assert.Equal(text, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task An_empty_blob_decodes_to_no_bytes()
    {
        var transport = new RoutingTransport().Returns("git/blobs", """{ "encoding": "base64", "content": "" }""");

        var bytes = await new GitHubTreeClient(transport).ReadBlobAsync(Repository, "b1", Token);

        Assert.Empty(bytes);
    }

    [Fact]
    public async Task A_blob_in_an_encoding_this_app_cannot_read_is_refused_with_a_reason()
    {
        var transport = new RoutingTransport().Returns("git/blobs", """{ "encoding": "punycode", "content": "x" }""");

        var exception = await Assert.ThrowsAsync<GitHubException>(() =>
            new GitHubTreeClient(transport).ReadBlobAsync(Repository, "b1", Token));

        Assert.Contains("punycode", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_from_github_comes_through_as_is()
    {
        var transport = new RoutingTransport().Refuses("git/blobs", "GitHub refused access to JSdotNet/Backlog.");

        var exception = await Assert.ThrowsAsync<GitHubException>(() =>
            new GitHubTreeClient(transport).ReadBlobAsync(Repository, "b1", Token));

        Assert.Equal("GitHub refused access to JSdotNet/Backlog.", exception.Message);
    }
}
