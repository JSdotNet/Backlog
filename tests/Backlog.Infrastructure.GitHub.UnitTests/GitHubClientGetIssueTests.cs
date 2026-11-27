using Backlog.Infrastructure.GitHub;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// An issue snapshot says when it was read, and that moment comes from the injected clock
/// (app-defaults ADR 0005), so a test can tell a fresh snapshot from a stale one.
/// </summary>
public sealed class GitHubClientGetIssueTests
{
    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    [Fact]
    public async Task The_snapshot_is_stamped_with_the_injected_clock()
    {
        var retrieved = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var transport = new RoutingTransport()
            .Returns("issues/412/timeline", "[]")
            .Returns("issues/412", """{ "number": 412, "title": "A bug", "state": "open" }""");

        var snapshot = await new GitHubClient(transport, new FakeTimeProvider(retrieved))
            .GetIssueAsync(Repository, 412, TestContext.Current.CancellationToken);

        Assert.Equal(412, snapshot.Issue.Number);
        Assert.Equal(retrieved, snapshot.RetrievedAt);
    }
}
