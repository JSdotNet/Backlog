using Backlog.Infrastructure.GitHub;

namespace Backlog.Tests;

/// <summary>
/// A GitHub connection probe that always describes the same connection —
/// not connected, unless the test hands it the connection to report.
/// </summary>
/// <param name="connection">What the probe reports; not connected when omitted.</param>
internal sealed class StubProbe(GitHubConnection? connection = null) : IGitHubConnectionProbe
{
    public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(connection ?? new GitHubConnection(false, "Not connected."));

    public void Invalidate()
    {
    }
}
