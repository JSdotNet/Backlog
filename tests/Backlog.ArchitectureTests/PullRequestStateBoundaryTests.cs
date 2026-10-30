namespace Backlog.ArchitectureTests;

/// <summary>
/// Repository Management owns pull-request state, and every other reader reaches it
/// through the module's contract (local ADR 0042). These facts read the source of the
/// readers that used to reach the GitHub adapter on their own, so a pull-request read
/// that goes around the contract — and so around its one cache — fails here rather
/// than in a count of GitHub calls nobody looks at.
/// </summary>
public sealed class PullRequestStateBoundaryTests
{
    /// <summary>
    /// Progress's pull-request reader asks the contract, not the GitHub adapter (local
    /// ADR 0042 §5, V6): no adapter in <c>Infrastructure.FileSystem/Progress</c> that
    /// answers goal pull requests names <c>GitHubIntegration</c> or the GitHub adapter's
    /// namespace.
    /// </summary>
    [Fact]
    public void Progress_reads_pull_requests_through_the_contract()
    {
        var readers = System.IO.Directory
            .EnumerateFiles(RepositoryRoot.Directory("src", "Infrastructure", "Backlog.Infrastructure.FileSystem", "Progress"), "*GoalPullRequests*.cs")
            .ToList();

        Assert.NotEmpty(readers);
        Assert.All(readers, reader =>
        {
            var source = File.ReadAllText(reader);
            Assert.DoesNotContain("GitHubIntegration", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Backlog.Infrastructure.GitHub", source, StringComparison.Ordinal);
        });
    }
}
