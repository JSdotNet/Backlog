namespace Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

/// <summary>One repository whose pull requests could not be read, in the sentence
/// GitHub or the transport refused with.</summary>
public sealed record RepositoryReadFailure(string RepositoryFullName, string Message);

/// <summary>
/// The pull requests of several repositories, read together, and the repositories that
/// could not be read.
/// <para>
/// The failures travel beside the pull requests rather than as an exception, because the
/// repositories are independent: one that could not be read says nothing about the
/// others. A number its repository answered without is absent from
/// <see cref="PullRequests"/>, with its repository absent from <see cref="Failures"/>.
/// </para>
/// </summary>
public sealed record PullRequestListing(
    IReadOnlyList<PullRequestStatus> PullRequests,
    IReadOnlyList<RepositoryReadFailure> Failures)
{
    public static PullRequestListing Empty { get; } = new([], []);

    /// <summary>
    /// For an open list: the repositories, by <c>owner/name</c>, whose every open pull
    /// request it holds — read without a failure and short of the source's cap. A
    /// repository it does not name may have open pull requests it does not hold: one it
    /// failed, capped, or does not list at all (unregistered, archived). Empty for a read
    /// by number, which lists no repository.
    /// </summary>
    public IReadOnlyCollection<string> CompleteRepositories { get; init; } = [];
}

/// <summary>The recently merged pull requests of several repositories, read together,
/// and the ones that could not be read.</summary>
/// <param name="Truncated">The repositories, by <c>owner/name</c>, whose read stopped
/// at the page cap while still inside the window — said rather than hidden, so a busy
/// repository's list is not passed off as complete.</param>
public sealed record MergedPullRequestListing(
    IReadOnlyList<MergedPullRequest> PullRequests,
    IReadOnlyList<RepositoryReadFailure> Failures,
    IReadOnlyList<string> Truncated)
{
    public static MergedPullRequestListing Empty { get; } = new([], [], []);
}
