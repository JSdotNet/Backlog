using Backlog.Modules.RepositoryManagement.Abstractions;

namespace Backlog.Modules.RepositoryManagement.UnitTests;

/// <summary>
/// The pull requests feature key moved from Sessions to Repository Management with its
/// string unchanged (local ADR 0042): it is a persisted value in everybody's
/// <c>features.json</c>, and a new string would forget what they had switched.
/// </summary>
public sealed class RepositoryManagementFeaturesTests
{
    [Fact]
    public void The_pull_requests_key_keeps_the_value_it_was_stored_under()
    {
        Assert.Equal("pull-requests", RepositoryManagementFeatures.PullRequests);
    }
}
