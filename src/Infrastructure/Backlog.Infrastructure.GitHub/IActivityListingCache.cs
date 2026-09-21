namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// One merged pull request as the listing walk found it: the fields that come free
/// with the page, and nothing that costs a further call.
/// <para>
/// The title and url are here even though a pull request can be renamed after it
/// merges, and that is a smaller compromise than it looks: a rename is an update,
/// an update puts the pull request back at the top of the next walk, and the row
/// is overwritten with the new title. Nothing on the dashboard renders either
/// field today; they travel so the record stays the one the client already
/// produces.
/// </para>
/// </summary>
public sealed record ListedPullRequest(
    int Number,
    string Url,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset MergedAt);

/// <summary>One closed issue as the listing walk found it.</summary>
public sealed record ListedIssue(int Number, string Url, string Title, DateTimeOffset ClosedAt);

/// <summary>
/// How much of a repository's history one listing has walked.
/// </summary>
/// <param name="CoveredFrom">
/// The earliest instant every merged pull request (or closed issue) is known from.
/// A window that starts at or after this can be answered from the listing plus a
/// delta walk; one that starts before it has to walk back to its own start.
/// </param>
/// <param name="WalkedThrough">
/// The latest <c>updated_at</c> the last complete walk saw. Sorted by
/// <c>updated</c> descending, the next walk can stop at the first row older than
/// this: nothing that merged or closed since can have gone untouched since.
/// </param>
public sealed record ActivityListingCoverage(DateTimeOffset CoveredFrom, DateTimeOffset WalkedThrough);

/// <summary>
/// Everything a listing walk of one repository, for one author, has found so far,
/// and how far it is known to be complete.
/// <para>
/// Pull requests and issues carry separate coverage because they are separate
/// walks with separate page budgets: one can run out of pages while the other
/// finishes, and a shared watermark would let the truncated one advance on the
/// strength of the other's completeness.
/// </para>
/// <para>
/// Coverage is null until a walk has reached anything at all. Rows are kept from
/// every walk — a merged pull request is a fact whichever walk found it — and a
/// walk that ran out of pages covers only from the oldest row it reached, so a
/// listing never claims a window on the strength of pages nobody read.
/// </para>
/// </summary>
public sealed record ActivityListing(
    IReadOnlyList<ListedPullRequest> PullRequests,
    IReadOnlyList<ListedIssue> Issues,
    ActivityListingCoverage? PullRequestCoverage,
    ActivityListingCoverage? IssueCoverage)
{
    public static ActivityListing Empty { get; } = new([], [], null, null);
}

/// <summary>
/// Keeps the merged pull requests and closed issues a listing walk has already
/// found on disk, so a dashboard read walks only the pages that changed since.
/// <para>
/// The counterpart of <see cref="IPullRequestDetailCache"/> one step earlier in the
/// same fetch. That one saves the calls made <em>per</em> pull request; this one
/// saves the pages walked to find out which pull requests there are. Without it a
/// twelve-week window at a busy repository re-reads up to ten pages on every
/// dashboard open to rediscover a list that has, most days, gained one row.
/// </para>
/// <para>
/// Declared here and implemented in <c>Backlog.Infrastructure.FileSystem</c>, for
/// the reason <see cref="IPullRequestDetailCache"/> gives: the contract is phrased
/// in a <see cref="GitHubRepositoryRef"/>, and where the bytes land is the
/// workspace's decision.
/// </para>
/// <para>
/// Per author as well as per repository. A machine can hold more than one GitHub
/// identity now, and a merged pull request is a fact about the repository but
/// "yours" is a fact about who asked — a listing built for one login answered to
/// another would be somebody else's productivity.
/// </para>
/// <para>
/// Synchronous and non-throwing, like its sibling: an entry that cannot be read is
/// a miss and the window is walked in full, and one that cannot be written is
/// walked in full again next time. Wasteful, not wrong.
/// </para>
/// </summary>
public interface IActivityListingCache
{
    /// <summary>What is stored for this repository and author, or null when nothing
    /// is — including when what is stored cannot be read or was written by an
    /// older version of this app.</summary>
    ActivityListing? TryRead(GitHubRepositoryRef repository, string author);

    /// <summary>Replaces what is stored for this repository and author. Failure is
    /// not reported, because there is nothing a caller could usefully do about
    /// it.</summary>
    void Write(GitHubRepositoryRef repository, string author, ActivityListing listing);

    /// <summary>Drops everything stored for one repository, every author's listing
    /// included, so its next read walks the whole window again.</summary>
    void ForgetRepository(GitHubRepositoryRef repository);
}
