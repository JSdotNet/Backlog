namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// Everything about a merged pull request that costs calls to work out, and that
/// cannot change again once it is merged.
/// <para>
/// The listing fields — number, title, url, when it was created and merged — are
/// deliberately absent. They arrive free with the page walk that found the pull
/// request in the first place, and storing them would mean a cached title going
/// stale against a pull request somebody renamed.
/// </para>
/// <para>
/// <see cref="ChurnComplete"/> and <see cref="SizeKnown"/> travel with the numbers
/// rather than beside them, and that is the single property this whole cache turns
/// on. A floor that comes back as a total on the second run is worse than no cache
/// at all: the figure would be wrong and nothing on screen would say so.
/// </para>
/// </summary>
public sealed record PullRequestDetail
{
    /// <summary>When the first verdict arrived, or null when nobody reviewed it.</summary>
    public DateTimeOffset? FirstReviewedAt { get; init; }

    public int ReviewRounds { get; init; }

    public int ChangesRequested { get; init; }

    public int CommitsAfterFirstReview { get; init; }

    public int ForcePushesAfterFirstReview { get; init; }

    public int FilesRetouched { get; init; }

    /// <summary>False when <see cref="FilesRetouched"/> is a floor rather than a
    /// total.</summary>
    public bool ChurnComplete { get; init; }

    /// <summary>Additions plus deletions.</summary>
    public int ChangedLines { get; init; }

    public int ChangedFiles { get; init; }

    /// <summary>False when the size could not be read, in which case both numbers
    /// above are zero and mean nothing.</summary>
    public bool SizeKnown { get; init; }
}

/// <summary>
/// Keeps the churn and size detail of merged pull requests on disk, so a dashboard
/// read fetches only the pull requests it has not seen before.
/// <para>
/// The port is declared here and implemented in
/// <c>Backlog.Infrastructure.FileSystem</c>, which is exactly the arrangement
/// <see cref="IKnowledgeSnapshotCache"/> already lives on either side of and for
/// the same reason: a contract phrased in terms of a
/// <see cref="GitHubRepositoryRef"/> belongs where that type is, while the half
/// that decides where bytes land on disk belongs with the workspace. The
/// architecture tests enforce the direction — this adapter may not reach for that
/// one.
/// </para>
/// <para>
/// Only merged pull requests may be stored, and that is what makes this safe. A
/// merged pull request's reviews, its commits, and its diff are all frozen; an
/// open one's are not. The cost of reading it is what is being avoided, not the
/// freshness of the answer.
/// </para>
/// <para>
/// Synchronous and non-throwing on purpose. It sits inside the per-pull-request
/// batch of a fetch a person is waiting on, an entry that cannot be read is a
/// miss, and an entry that cannot be written is simply fetched again next time —
/// wasteful, not wrong.
/// </para>
/// </summary>
public interface IPullRequestDetailCache
{
    /// <summary>What is stored for this pull request, or null when nothing is —
    /// including when what is stored cannot be read or was written by an older
    /// version of this app.</summary>
    PullRequestDetail? TryRead(GitHubRepositoryRef repository, int number);

    /// <summary>Stores one merged pull request's detail. Failure is not
    /// reported, because there is nothing a caller could usefully do about
    /// it.</summary>
    void Write(GitHubRepositoryRef repository, int number, PullRequestDetail detail);

    /// <summary>Drops everything stored for one repository, so its next read goes
    /// back to GitHub for every pull request. The way back when the stored copy is
    /// wrong — a repository whose history was rewritten, or a figure somebody does
    /// not believe.</summary>
    void ForgetRepository(GitHubRepositoryRef repository);
}
