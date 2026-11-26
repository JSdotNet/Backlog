using Backlog.Modules.Repositories.Abstractions;

namespace Backlog.Modules.Repositories.UnitTests;

/// <summary>
/// The pull requests feature key moved from Sessions to Repositories with its
/// string unchanged (local ADR 0042): it is a persisted value in everybody's
/// <c>features.json</c>, and a new string would forget what they had switched.
/// </summary>
public sealed class RepositoriesFeaturesTests
{
    [Fact]
    public void The_pull_requests_key_keeps_the_value_it_was_stored_under()
    {
        Assert.Equal("pull-requests", RepositoriesFeatures.PullRequests);
    }
}
