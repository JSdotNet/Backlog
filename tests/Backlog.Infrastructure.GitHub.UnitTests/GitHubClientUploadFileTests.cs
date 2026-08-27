using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// Committing a file through the Contents API: the branch it lands on may
/// already exist or may need creating off the default branch first, and the
/// only thing worth linking anywhere afterwards is the URL GitHub hands back.
/// </summary>
public sealed class GitHubClientUploadFileTests
{
    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    [Fact]
    public async Task An_existing_branch_is_committed_to_directly()
    {
        var transport = new RoutingTransport()
            .Returns("git/ref/heads/feedback-screenshots", """{ "ref": "refs/heads/feedback-screenshots" }""")
            .Returns("contents/feedback-screenshots%2Fshot.jpg", """{ "content": { "download_url": "https://raw.githubusercontent.com/JSdotNet/Backlog/feedback-screenshots/feedback-screenshots/shot.jpg" } }""");
        var client = new GitHubClient(transport);

        var uploaded = await client.UploadFileAsync(
            Repository, "feedback-screenshots/shot.jpg", "feedback-screenshots", [1, 2, 3], "Add feedback screenshot");

        Assert.Equal("feedback-screenshots/shot.jpg", uploaded.Path);
        Assert.Equal(
            "https://raw.githubusercontent.com/JSdotNet/Backlog/feedback-screenshots/feedback-screenshots/shot.jpg",
            uploaded.DownloadUrl);

        // No branch was created: the ref lookup answering successfully was enough.
        Assert.Equal(0, transport.CallsTo("git/refs"));
    }

    [Fact]
    public async Task A_missing_branch_is_created_off_the_default_branch_before_the_commit()
    {
        // Order matters: RoutingTransport matches the first route whose fragment
        // is a substring of the path, so the bare repository-details route has to
        // come after every more specific one or it would swallow them all — every
        // path here starts with "repos/JSdotNet/Backlog".
        var transport = new RoutingTransport()
            .Refuses("git/ref/heads/feedback-screenshots", "Not Found")
            .Returns("git/ref/heads/main", """{ "object": { "sha": "abc123" } }""")
            .Returns("git/refs", """{ "ref": "refs/heads/feedback-screenshots" }""")
            .Returns("contents/feedback-screenshots%2Fshot.jpg", """{ "content": { "download_url": "https://raw.githubusercontent.com/JSdotNet/Backlog/feedback-screenshots/feedback-screenshots/shot.jpg" } }""")
            .Returns("repos/JSdotNet/Backlog", """{ "default_branch": "main" }""");
        var client = new GitHubClient(transport);

        var uploaded = await client.UploadFileAsync(
            Repository, "feedback-screenshots/shot.jpg", "feedback-screenshots", [1, 2, 3], "Add feedback screenshot");

        Assert.Equal(
            "https://raw.githubusercontent.com/JSdotNet/Backlog/feedback-screenshots/feedback-screenshots/shot.jpg",
            uploaded.DownloadUrl);
        Assert.Equal(1, transport.CallsTo("git/refs"));
    }

    [Fact]
    public async Task A_response_with_no_download_url_is_reported_rather_than_returned_as_empty()
    {
        var transport = new RoutingTransport()
            .Returns("git/ref/heads/feedback-screenshots", """{ "ref": "refs/heads/feedback-screenshots" }""")
            .Returns("contents/feedback-screenshots%2Fshot.jpg", """{ "content": {} }""");
        var client = new GitHubClient(transport);

        await Assert.ThrowsAsync<GitHubException>(() => client.UploadFileAsync(
            Repository, "feedback-screenshots/shot.jpg", "feedback-screenshots", [1, 2, 3], "Add feedback screenshot"));
    }
}
