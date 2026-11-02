using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A pull request as a test says GitHub holds it, read by number: what it is called,
/// where it stands, who opened it, when it merged, its checks and its reviews.
/// <see cref="TasksPaneHost.FakeGitHubClient"/> answers the pinned read and the REST read
/// from these. A fixture's shape, not a GitHub one: the adapter's own read by number is
/// the pinned read (local ADR 0042).
/// </summary>
internal sealed record PullRequestByNumber(
    int Number,
    string Url,
    string Title,
    string RepositoryFullName,
    GitHubItemState State,
    string? AuthorLogin,
    DateTimeOffset? MergedAt,
    DateTimeOffset? CreatedAt = null)
{
    public GitHubCheckCounts? CheckCounts { get; init; }

    public IReadOnlyList<string> FailingChecks { get; init; } = [];

    public GitHubReviewSummary Reviews { get; init; } = GitHubReviewSummary.None;

    public string? BaseRefName { get; init; }

    public string? DefaultBranch { get; init; }
}
