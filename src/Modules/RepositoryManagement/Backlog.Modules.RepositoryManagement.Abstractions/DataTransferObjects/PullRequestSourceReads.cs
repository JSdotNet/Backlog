namespace Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

/// <summary>One repository's pull requests as the source read them, or the refusal
/// that kept them unread. A number the repository answered without is absent, with no
/// failure.</summary>
public sealed record PullRequestSourceRead(
    IReadOnlyList<PullRequestStatus> PullRequests,
    RepositoryReadFailure? Failure)
{
    /// <summary>For an open list: it came back as long as the source asks for, so the
    /// repository may have older open pull requests it does not hold.</summary>
    public bool Truncated { get; init; }
}

/// <summary>One repository's recently merged pull requests as the source read them, or
/// the refusal that kept them unread.</summary>
/// <param name="Truncated">The read stopped at the source's page cap while still inside
/// the window.</param>
public sealed record MergedPullRequestSourceRead(
    IReadOnlyList<MergedPullRequest> PullRequests,
    RepositoryReadFailure? Failure,
    bool Truncated);
