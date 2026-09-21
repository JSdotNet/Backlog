using System.Runtime.ExceptionServices;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// One merged pull request, with what post-review churn is counted from.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FirstReviewedAt"/> null means nobody reviewed it. That is a different
/// fact from "reviewed and never churned", and the two must not collapse: the first
/// is not evidence of clean work, the second is.
/// </para>
/// <para>
/// <see cref="ChurnComplete"/> false means the per-commit file inspection stopped
/// before it ran out of commits, so <see cref="FilesRetouched"/> is a floor rather
/// than a total. Carried rather than hidden — a capped figure that reads as a whole
/// one is how a dashboard stops being trusted.
/// </para>
/// </remarks>
public sealed record GitHubReviewedPullRequest(
    int Number,
    string Url,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset MergedAt,
    DateTimeOffset? FirstReviewedAt,
    int ReviewRounds,
    int ChangesRequested,
    int CommitsAfterFirstReview,
    int ForcePushesAfterFirstReview,
    int FilesRetouched,
    bool ChurnComplete)
{
    /// <summary>How long the first review took to arrive after the pull request was
    /// opened. Null when there was no review.</summary>
    public TimeSpan? ReviewTurnaround =>
        FirstReviewedAt is { } reviewed ? reviewed - CreatedAt : null;

    /// <summary>Additions plus deletions. Meaningless unless
    /// <see cref="SizeKnown"/>.</summary>
    public int ChangedLines { get; init; }

    /// <summary>Files the diff touches. Meaningless unless
    /// <see cref="SizeKnown"/>.</summary>
    public int ChangedFiles { get; init; }

    /// <summary>
    /// Whether the size above was actually read.
    /// <para>
    /// False and zero, rather than absent, for the reason
    /// <see cref="ChurnComplete"/> exists: a pull request whose detail could not be
    /// fetched still happened, so it stays in the listing, and the one thing that
    /// must not happen is its zero being averaged in as a very small pull request.
    /// </para>
    /// </summary>
    public bool SizeKnown { get; init; }

    /// <summary>Every commit on the pull request. Read off the same call as the size,
    /// so meaningful exactly when <see cref="SizeKnown"/>.</summary>
    public int Commits { get; init; }

    /// <summary>Merge commits on the branch — each one a sync with the base branch
    /// (or, rarely, with another branch). Meaningless unless
    /// <see cref="SyncsKnown"/>.</summary>
    public int SyncMerges { get; init; }

    /// <summary>
    /// How many of <see cref="SyncMerges"/> say they resolved a conflict.
    /// <para>
    /// GitHub keeps no record of a conflict once the merge that resolved it is
    /// committed, so this is read from the merge commit's own message. Git writes
    /// a <c>Conflicts:</c> trailer into it, which survives a <c>--no-edit</c>
    /// commit, and a merge somebody wrote up by hand names what conflicted; a merge
    /// whose message says nothing reads as clean, so this is a floor.
    /// </para>
    /// </summary>
    public int ConflictedSyncMerges { get; init; }

    /// <summary>Whether the branch's commits were actually read. False and zero,
    /// rather than absent, for the reason <see cref="SizeKnown"/> is: a pull
    /// request whose commits could not be listed still merged, and its zero must
    /// not be counted as a clean sync.</summary>
    public bool SyncsKnown { get; init; }
}

/// <summary>One closed issue. Pull requests are excluded — GitHub's issues
/// endpoint returns both, and counting a merged pull request as a closed issue
/// would double every figure on the dashboard.</summary>
public sealed record GitHubClosedIssue(int Number, string Url, string Title, DateTimeOffset ClosedAt);

/// <summary>What one repository's activity fetch produced.</summary>
public sealed record GitHubRepositoryActivity(
    string RepositoryFullName,
    IReadOnlyList<GitHubReviewedPullRequest> PullRequests,
    IReadOnlyList<GitHubClosedIssue> Issues)
{
    public static GitHubRepositoryActivity Empty(string fullName) => new(fullName, [], []);

    /// <summary>
    /// Whether the pull-request walk reached the end of what it was looking for.
    /// <para>
    /// False means it stopped because it ran out of pages it was willing to read,
    /// not because it ran out of pull requests — so <see cref="PullRequests"/> is a
    /// prefix of the answer and every figure derived from it is a floor. This is
    /// not hypothetical: at a busy repository's volume a twelve-week window
    /// exhausts the page budget, and before this flag existed it did so silently.
    /// </para>
    /// </summary>
    public bool ListingComplete { get; init; } = true;

    /// <summary>
    /// Whether every listed pull request's size and commits could be read. False
    /// when at least one of them has <c>SizeKnown</c> or <c>SyncsKnown</c> false,
    /// so a size average knows it is missing rows rather than averaging in zeroes,
    /// and a sync proportion knows it is missing pull requests rather than counting
    /// them as never synced.
    /// </summary>
    public bool DetailComplete { get; init; } = true;
}

/// <summary>Why activity reporting is or is not usable, in words fit for a screen.</summary>
public sealed record GitHubActivityAvailability(bool IsAvailable, string Reason);

/// <summary>
/// The activity questions the dashboard asks GitHub: what did this person merge
/// and close, and how much of it came back after review.
/// </summary>
public interface IGitHubActivityClient
{
    Task<GitHubActivityAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// One repository's merged pull requests and closed issues authored by
    /// <paramref name="author"/>, between two instants.
    /// </summary>
    Task<GitHubRepositoryActivity> GetActivityAsync(
        GitHubRepositoryRef repository,
        DateTimeOffset from,
        DateTimeOffset to,
        string author,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IGitHubActivityClient"/> over the same transport the issue client
/// uses, so <c>gh</c> and token authentication both work without a second
/// credential.
/// </summary>
/// <remarks>
/// <para>
/// Every figure here is derived from what GitHub reports rather than inferred.
/// "Rework" in this product means post-review churn, and the three measures below
/// are the three GitHub can actually answer for: commits whose time is after the
/// first review, <c>head_ref_force_pushed</c> timeline events after it, and files
/// touched both before and after it.
/// </para>
/// <para>
/// The call budget is the reason this class is shaped the way it is. Listing costs
/// two calls per repository; each pull request then costs four more, and files
/// re-touched costs one per post-review commit on top. So the per-pull-request work
/// only happens for pull requests that reached the window, and the file inspection
/// is capped — with the cap reported rather than swallowed.
/// </para>
/// <para>
/// The largest saving is <paramref name="details"/>. A merged pull request's
/// reviews, commits and diff are frozen, so the per-pull-request work is done once
/// and remembered; a second read of the same window costs the two listing calls and
/// nothing else. It is optional because it is an optimization — the client answers
/// the same thing without it, only slower.
/// </para>
/// <para>
/// <paramref name="listings"/> takes the two listing calls down as well. What was
/// merged and what was closed are facts once they have happened, so the rows a
/// walk found are kept and the next walk reads only the pages touched since — see
/// <see cref="IActivityListingCache"/>. Optional for the same reason, and the two
/// caches are independent: either one alone is a correct client that makes more
/// calls than it needs to.
/// </para>
/// </remarks>
public sealed class GitHubActivityClient(
    IGitHubTransport transport,
    IPullRequestDetailCache? details = null,
    IActivityListingCache? listings = null)
    : IGitHubActivityClient
{
    /// <summary>GitHub caps a list page at 100.</summary>
    private const int PageSize = 100;

    /// <summary>Paging is bounded rather than trusted to terminate.</summary>
    private const int MaxPages = 10;

    /// <summary>
    /// How far behind its last high-water mark a delta walk starts. GitHub's list
    /// endpoints are eventually consistent by a few seconds, and a row updated in
    /// the same second the last walk read its first page could sort either side
    /// of it. Five minutes re-reads a handful of rows to make sure of that; a walk
    /// that started exactly at the mark would occasionally miss one and never know.
    /// </summary>
    private static readonly TimeSpan WalkSlack = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How many post-review commits are inspected for re-touched files. Past this
    /// the figure becomes a floor and <c>ChurnComplete</c> goes false. Twenty is
    /// well above a normal review cycle and still bounds a pathological pull
    /// request to twenty extra calls rather than two hundred.
    /// </summary>
    private const int MaxChurnCommits = 20;

    /// <summary>
    /// How many pull requests have their churn read at once.
    /// </summary>
    /// <remarks>
    /// The reason this exists at all is the <c>gh</c> CLI transport: it does not make
    /// an HTTP call, it launches a process, and a process launch is the better part of
    /// a second. Twenty merged pull requests at three calls each is sixty launches, and
    /// sequentially that is a minute of somebody watching a spinner. Four at a time
    /// takes the obvious bite out of that without turning a dashboard opening into
    /// twenty concurrent processes.
    /// </remarks>
    private const int MaxConcurrentPullRequests = 4;

    public async Task<GitHubActivityAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        await transport.IsAvailableAsync(cancellationToken).ConfigureAwait(false)
            ? new GitHubActivityAvailability(true, $"Reading pull requests and issues with the {transport.Description}.")
            : new GitHubActivityAvailability(
                false,
                "Backlog cannot reach GitHub. Sign in with `gh auth login`, or add a personal access token in "
                + "repository settings.");

    public async Task<GitHubRepositoryActivity> GetActivityAsync(
        GitHubRepositoryRef repository,
        DateTimeOffset from,
        DateTimeOffset to,
        string author,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);

        // Read once, before either walk starts, so both see the same picture of what
        // was known and neither can overwrite the other's rows with a stale copy.
        var remembered = listings?.TryRead(repository, author) ?? ActivityListing.Empty;

        // Concurrently: the two listings share nothing, and one waiting on the other is
        // a second of a person's time for no reason.
        var pullRequests = ReadPullRequestsAsync(repository, from, to, author, remembered, cancellationToken);
        var issues = ReadIssuesAsync(repository, from, to, author, remembered, cancellationToken);

        await Task.WhenAll(pullRequests, issues).ConfigureAwait(false);

        var listing = await pullRequests.ConfigureAwait(false);
        var closed = await issues.ConfigureAwait(false);

        // Written whether or not either walk completed. The rows are facts whichever
        // walk found them; only the coverage says how far they can be trusted, and
        // an incomplete walk leaves that where it was.
        listings?.Write(repository, author, new ActivityListing(
            listing.Listed,
            closed.Listed,
            listing.Coverage,
            closed.Coverage));

        return new GitHubRepositoryActivity(
            repository.FullName,
            listing.PullRequests,
            closed.Issues)
        {
            ListingComplete = listing.Complete,
            DetailComplete = listing.PullRequests.All(pull => pull.SizeKnown && pull.SyncsKnown)
        };
    }

    /// <summary>
    /// Where a walk may stop: behind the last complete walk's high-water mark when
    /// the window lies inside what that walk covered, and at the window's own start
    /// when it does not.
    /// <para>
    /// Sorted by <c>updated</c> descending, a walk that stops at the first row older
    /// than this has seen every row touched since — and a pull request cannot merge,
    /// nor an issue close, without being touched.
    /// </para>
    /// </summary>
    private static (DateTimeOffset StopAt, bool Delta) WhereToStop(ActivityListingCoverage? coverage, DateTimeOffset from) =>
        coverage is not null && coverage.CoveredFrom <= from
            ? (coverage.WalkedThrough - WalkSlack, true)
            : (from, false);

    /// <summary>
    /// The coverage after a walk.
    /// <para>
    /// A complete walk covers back to where it was told to stop — the window's
    /// start, or on a delta walk whatever was covered before — and forward to the
    /// newest row it saw, never retreating from the mark the last walk left.
    /// </para>
    /// <para>
    /// A walk that ran out of pages still proves something, and it is worth
    /// keeping: sorted by <c>updated</c> descending, every row touched at or after
    /// the oldest one it reached has been read. So it covers from that row — plus
    /// the slack, for a tie split across a page boundary — and no earlier, whatever
    /// was covered before; a merge that landed in the pages it did not reach would
    /// otherwise be trusted on the strength of a walk that never saw it. This is
    /// what lets a busy repository whose twelve-week window exhausts the page
    /// budget still answer its four-week window as a delta.
    /// </para>
    /// </summary>
    private static ActivityListingCoverage? CoverageAfter(
        ActivityListingCoverage? before,
        bool complete,
        bool delta,
        DateTimeOffset from,
        DateTimeOffset stopAt,
        DateTimeOffset? newest,
        DateTimeOffset? oldest)
    {
        if (!complete && oldest is null) return before;

        var coveredFrom = complete
            ? delta ? before!.CoveredFrom : from
            : oldest!.Value + WalkSlack;

        var walkedThrough = newest ?? stopAt;

        if (before is not null && before.WalkedThrough > walkedThrough) walkedThrough = before.WalkedThrough;

        return new ActivityListingCoverage(coveredFrom, walkedThrough);
    }

    /// <summary>
    /// Merged pull requests in the window, newest first, then the churn detail for
    /// each.
    /// </summary>
    /// <remarks>
    /// Listed and filtered here rather than through the search API. Search would
    /// express the window and the author in one query, but it is rate-limited far
    /// more tightly than the list endpoints, its results lag behind by up to a
    /// minute, and it caps at a thousand hits with no way to tell that it did.
    /// Sorting by <c>updated</c> descending lets the walk stop as soon as it is past
    /// the window instead of reading the whole history.
    /// </remarks>
    private async Task<PullRequestWalk> ReadPullRequestsAsync(
        GitHubRepositoryRef repository,
        DateTimeOffset from,
        DateTimeOffset to,
        string author,
        ActivityListing remembered,
        CancellationToken cancellationToken)
    {
        var (stopAt, delta) = WhereToStop(remembered.PullRequestCoverage, from);

        // Keyed by number so a row the last walk already had is overwritten rather
        // than doubled — a pull request touched since is listed again, and the
        // listing copy is the newer one.
        var known = remembered.PullRequests.ToDictionary(pull => pull.Number);
        DateTimeOffset? newest = null;
        DateTimeOffset? oldest = null;

        // Set on every way out of the walk except one: falling off the end of the
        // page budget. That is the case this exists for, and it is the only one
        // where the list is a prefix rather than an answer.
        var complete = false;

        for (var page = 1; page <= MaxPages; page++)
        {
            var response = await transport.SendAsync(
                HttpMethod.Get,
                $"repos/{repository.Owner}/{repository.Name}/pulls"
                    + $"?state=closed&sort=updated&direction=desc&per_page={PageSize}&page={page}",
                body: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.ValueKind != JsonValueKind.Array)
            {
                complete = true;
                break;
            }

            var rows = response.EnumerateArray().ToList();
            if (rows.Count == 0)
            {
                complete = true;
                break;
            }

            var pastWindow = false;

            foreach (var row in rows)
            {
                var updated = Timestamp(row, "updated_at");

                // Sorted by updated descending, so once a row was last touched
                // before the stop nothing after it can be inside the window — or,
                // on a delta walk, can have changed since the last one.
                if (updated is { } stamp && stamp < stopAt)
                {
                    pastWindow = true;
                    break;
                }

                if (updated is { } seen)
                {
                    if (newest is null || seen > newest) newest = seen;
                    if (oldest is null || seen < oldest) oldest = seen;
                }

                if (Timestamp(row, "merged_at") is not { } mergedAt) continue;
                if (!IsAuthor(row, author)) continue;

                // Kept whether or not it is inside this window. A merged pull
                // request is a fact, and one merged before the window that was
                // touched recently is exactly the row a later, wider window would
                // otherwise have to walk back for.
                known[Number(row, "number")] = new ListedPullRequest(
                    Number(row, "number"),
                    String(row, "html_url") ?? string.Empty,
                    String(row, "title") ?? string.Empty,
                    Timestamp(row, "created_at") ?? mergedAt,
                    mergedAt);
            }

            if (pastWindow || rows.Count < PageSize)
            {
                complete = true;
                break;
            }
        }

        var listed = known.Values.OrderByDescending(pull => pull.MergedAt).ToList();

        var merged = listed
            .Where(pull => pull.MergedAt >= from && pull.MergedAt <= to)
            .ToList();

        var detailed = new List<GitHubReviewedPullRequest>(merged.Count);

        // In batches rather than one at a time, and in batches rather than all at once
        // — see MaxConcurrentPullRequests. Order is preserved because the batches are
        // walked in order and each batch keeps its own.
        foreach (var batch in merged.Chunk(MaxConcurrentPullRequests))
        {
            var read = await Task
                .WhenAll(batch.Select(pullRequest => ReadOrRecallAsync(repository, pullRequest, cancellationToken)))
                .ConfigureAwait(false);

            detailed.AddRange(read);
        }

        return new PullRequestWalk(
            detailed,
            complete,
            listed,
            CoverageAfter(remembered.PullRequestCoverage, complete, delta, from, stopAt, newest, oldest));
    }

    /// <summary>What one pull-request walk produced: the window's answer, and the
    /// listing to remember for the next one.</summary>
    private sealed record PullRequestWalk(
        IReadOnlyList<GitHubReviewedPullRequest> PullRequests,
        bool Complete,
        IReadOnlyList<ListedPullRequest> Listed,
        ActivityListingCoverage? Coverage);

    /// <summary>The issue counterpart of <see cref="PullRequestWalk"/>.</summary>
    private sealed record IssueWalk(
        IReadOnlyList<GitHubClosedIssue> Issues,
        IReadOnlyList<ListedIssue> Listed,
        ActivityListingCoverage? Coverage);

    /// <summary>
    /// One pull request's detail, from the cache when it is there and from GitHub
    /// when it is not.
    /// <para>
    /// Only what was actually fetched, whole, is written back. A cached entry
    /// that came back is not rewritten, because it would be rewritten
    /// identically — the whole reason this is cacheable is that a merged pull
    /// request's answer does not move. A read GitHub refused in part is not
    /// written either, for the same reason turned around: the cache has no age,
    /// so a refusal remembered would be served for as long as the repository is
    /// configured, and the rate limit that refused one open would keep every
    /// later open reporting a floor.
    /// </para>
    /// </summary>
    private async Task<GitHubReviewedPullRequest> ReadOrRecallAsync(
        GitHubRepositoryRef repository,
        ListedPullRequest pullRequest,
        CancellationToken cancellationToken)
    {
        if (details?.TryRead(repository, pullRequest.Number) is { } remembered)
        {
            return new GitHubReviewedPullRequest(
                pullRequest.Number,
                pullRequest.Url,
                pullRequest.Title,
                pullRequest.CreatedAt,
                pullRequest.MergedAt,
                remembered.FirstReviewedAt,
                remembered.ReviewRounds,
                remembered.ChangesRequested,
                remembered.CommitsAfterFirstReview,
                remembered.ForcePushesAfterFirstReview,
                remembered.FilesRetouched,

                // Carried, never assumed. A floor that came back as a total would
                // make the second read of a window quietly more confident than the
                // first, which is the worst thing a cache can do to a figure.
                remembered.ChurnComplete)
            {
                ChangedLines = remembered.ChangedLines,
                ChangedFiles = remembered.ChangedFiles,
                SizeKnown = remembered.SizeKnown,
                Commits = remembered.Commits,
                SyncMerges = remembered.SyncMerges,
                ConflictedSyncMerges = remembered.ConflictedSyncMerges,
                SyncsKnown = remembered.SyncsKnown
            };
        }

        var read = await ReadChurnAsync(repository, pullRequest, cancellationToken).ConfigureAwait(false);

        if (!read.SizeKnown || !read.SyncsKnown) return read;

        details?.Write(repository, read.Number, new PullRequestDetail
        {
            FirstReviewedAt = read.FirstReviewedAt,
            ReviewRounds = read.ReviewRounds,
            ChangesRequested = read.ChangesRequested,
            CommitsAfterFirstReview = read.CommitsAfterFirstReview,
            ForcePushesAfterFirstReview = read.ForcePushesAfterFirstReview,
            FilesRetouched = read.FilesRetouched,
            ChurnComplete = read.ChurnComplete,
            ChangedLines = read.ChangedLines,
            ChangedFiles = read.ChangedFiles,
            SizeKnown = read.SizeKnown,
            Commits = read.Commits,
            SyncMerges = read.SyncMerges,
            ConflictedSyncMerges = read.ConflictedSyncMerges,
            SyncsKnown = read.SyncsKnown
        });

        return read;
    }

    /// <summary>
    /// The churn detail for one pull request: when it was first reviewed, how many
    /// rounds it took, and what happened after that first review.
    /// </summary>
    private async Task<GitHubReviewedPullRequest> ReadChurnAsync(
        GitHubRepositoryRef repository,
        ListedPullRequest pullRequest,
        CancellationToken cancellationToken)
    {
        var prefix = $"repos/{repository.Owner}/{repository.Name}";

        // Issued alongside the reviews rather than after them. The size lives on
        // the pull request itself, which the listing does not carry, so it costs a
        // call — but it costs no wall clock, because the reviews call is already in
        // flight and this one waits beside it. The commits go out in the same
        // breath: they used to wait for the reviews, because only a reviewed pull
        // request had an "after the review" to look in, but the branch's sync
        // merges are on every pull request whether anybody reviewed it or not.
        var reviewsCall = ReadArrayAsync(
            $"{prefix}/pulls/{pullRequest.Number}/reviews?per_page={PageSize}",
            cancellationToken);

        var sizeCall = ReadSizeAsync($"{prefix}/pulls/{pullRequest.Number}", cancellationToken);

        var commitsCall = ReadCommitsAsync(
            $"{prefix}/pulls/{pullRequest.Number}/commits?per_page={PageSize}",
            cancellationToken);

        await Task.WhenAll(reviewsCall, sizeCall, commitsCall).ConfigureAwait(false);

        var reviews = await reviewsCall.ConfigureAwait(false);
        var size = await sizeCall.ConfigureAwait(false);
        var (commits, commitsRefusal) = await commitsCall.ConfigureAwait(false);

        var syncs = CountSyncMerges(commits);

        // A review of state COMMENTED is a comment, not a verdict, and counting it
        // as a round would make every conversation look like rework.
        var verdicts = reviews
            .Where(review => Verdict(String(review, "state")))
            .Select(review => Timestamp(review, "submitted_at"))
            .OfType<DateTimeOffset>()
            .OrderBy(instant => instant)
            .ToList();

        var firstReviewedAt = verdicts.Count == 0 ? (DateTimeOffset?)null : verdicts[0];

        var changesRequested = reviews.Count(review =>
            string.Equals(String(review, "state"), "CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase));

        if (firstReviewedAt is null)
        {
            // Nothing was reviewed, so there is no "after the review" to look in.
            // The timeline call is saved, and the record says plainly that it had
            // no review rather than that it had no churn. A refused commits call
            // is not fatal here — the churn figures need nothing from it — so it
            // reads as syncs unknown rather than as a pull request that never
            // synced.
            return new GitHubReviewedPullRequest(
                pullRequest.Number,
                pullRequest.Url,
                pullRequest.Title,
                pullRequest.CreatedAt,
                pullRequest.MergedAt,
                FirstReviewedAt: null,
                ReviewRounds: 0,
                ChangesRequested: changesRequested,
                CommitsAfterFirstReview: 0,
                ForcePushesAfterFirstReview: 0,
                FilesRetouched: 0,
                ChurnComplete: true)
            {
                ChangedLines = size.Lines,
                ChangedFiles = size.Files,
                SizeKnown = size.Known,
                Commits = size.Commits,
                SyncMerges = syncs.Merges,
                ConflictedSyncMerges = syncs.Conflicted,
                SyncsKnown = commitsRefusal is null
            };
        }

        // A reviewed pull request's churn is counted from its commits, and a
        // repository whose commits cannot be listed is a repository this client
        // cannot answer for — the same refusal it has always been, only raised
        // after the calls beside it finished rather than before they started.
        commitsRefusal?.Throw();

        var dated = commits
            .Select(commit => (Sha: String(commit, "sha"), At: CommitInstant(commit)))
            .Where(commit => commit.Sha is not null && commit.At is not null)
            .Select(commit => (Sha: commit.Sha!, At: commit.At!.Value))
            .OrderBy(commit => commit.At)
            .ToList();

        var after = dated.Where(commit => commit.At > firstReviewedAt).ToList();
        var before = dated.Where(commit => commit.At <= firstReviewedAt).ToList();

        var forcePushes = await CountForcePushesAsync(
            $"{prefix}/issues/{pullRequest.Number}/timeline?per_page={PageSize}",
            firstReviewedAt.Value,
            cancellationToken).ConfigureAwait(false);

        var (retouched, complete) = await CountRetouchedAsync(
            prefix,
            before,
            after,
            cancellationToken).ConfigureAwait(false);

        return new GitHubReviewedPullRequest(
            pullRequest.Number,
            pullRequest.Url,
            pullRequest.Title,
            pullRequest.CreatedAt,
            pullRequest.MergedAt,
            firstReviewedAt,
            ReviewRounds: verdicts.Count,
            ChangesRequested: changesRequested,
            CommitsAfterFirstReview: after.Count,
            ForcePushesAfterFirstReview: forcePushes,
            FilesRetouched: retouched,
            ChurnComplete: complete)
        {
            ChangedLines = size.Lines,
            ChangedFiles = size.Files,
            SizeKnown = size.Known,
            Commits = size.Commits,
            SyncMerges = syncs.Merges,
            ConflictedSyncMerges = syncs.Conflicted,
            SyncsKnown = true
        };
    }

    /// <summary>
    /// One pull request's commits, or the refusal that stood in for them.
    /// <para>
    /// The refusal is handed back rather than thrown because what it means depends
    /// on something the caller has not learned yet: for an unreviewed pull request
    /// it costs only the sync figures, for a reviewed one it costs the churn
    /// figures too, and only the reviews say which. Captured rather than caught
    /// and re-thrown so that the stack it is eventually raised with is the one it
    /// was raised with.
    /// </para>
    /// </summary>
    private async Task<(List<JsonElement> Commits, ExceptionDispatchInfo? Refusal)> ReadCommitsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await ReadArrayAsync(path, cancellationToken).ConfigureAwait(false), null);
        }
        catch (GitHubException exception)
        {
            return ([], ExceptionDispatchInfo.Capture(exception));
        }
    }

    /// <summary>
    /// The merge commits among a pull request's commits, and how many of them say
    /// they resolved a conflict.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A commit with two parents on a pull request branch is a sync — the base
    /// branch merged in, almost always. GitHub has no record of whether that sync
    /// conflicted once it is committed, so the message is the evidence: git's own
    /// <c>Conflicts:</c> trailer, which a <c>--no-edit</c> commit keeps, or a
    /// hand-written body that says what conflicted. A merge whose message is silent
    /// reads as clean, which makes the conflicted count a floor rather than a
    /// total.
    /// </para>
    /// <para>
    /// The one thing the match refuses is a message that says there was no
    /// conflict. It is the phrase a person reaches for when a merge went cleanly
    /// and they want to say so, and counting it would turn every reassurance
    /// into its opposite.
    /// </para>
    /// </remarks>
    private static (int Merges, int Conflicted) CountSyncMerges(IReadOnlyList<JsonElement> commits)
    {
        var merges = 0;
        var conflicted = 0;

        foreach (var commit in commits)
        {
            if (!commit.TryGetProperty("parents", out var parents)
                || parents.ValueKind != JsonValueKind.Array
                || parents.GetArrayLength() < 2)
            {
                continue;
            }

            merges++;

            if (commit.TryGetProperty("commit", out var detail) && MentionsConflict(String(detail, "message"))) conflicted++;
        }

        return (merges, conflicted);
    }

    private static bool MentionsConflict(string? message) =>
        message is not null
        && ConflictMention.IsMatch(message)
        && !ConflictDenial.IsMatch(message);

    private static readonly Regex ConflictMention = new(@"\bconflict", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ConflictDenial = new(
        @"\b(no|without|zero)\s+(merge\s+)?conflicts?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// How big one pull request's diff is, from the pull request itself.
    /// <para>
    /// A refusal is not a failure of the pull request. The pull request was merged
    /// whatever this endpoint says, so it stays in the listing with its size
    /// declared unknown rather than declared zero — a zero here would be averaged
    /// in as a very small pull request and pull every size figure down.
    /// </para>
    /// </summary>
    private async Task<(int Lines, int Files, int Commits, bool Known)> ReadSizeAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await transport
                .SendAsync(HttpMethod.Get, path, body: null, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (response.ValueKind != JsonValueKind.Object) return (0, 0, 0, false);

            // The commit count rides on the same response as the size, so it costs
            // nothing to carry and is known exactly when the size is.
            return (
                Number(response, "additions") + Number(response, "deletions"),
                Number(response, "changed_files"),
                Number(response, "commits"),
                true);
        }
        catch (GitHubException)
        {
            return (0, 0, 0, false);
        }
    }

    /// <summary>
    /// Files touched both before and after the first review — the closest GitHub
    /// gets to "this had to be done again".
    /// </summary>
    /// <remarks>
    /// <para>
    /// An intersection rather than a count of files in the later commits: a pull
    /// request that grew a new file after review added work, it did not redo any.
    /// </para>
    /// <para>
    /// Costs one call per commit, so both sides are capped and the second return
    /// value says whether the cap bit. Returns zero and complete when there was no
    /// churn at all, which is the common case and costs nothing.
    /// </para>
    /// </remarks>
    private async Task<(int Retouched, bool Complete)> CountRetouchedAsync(
        string prefix,
        IReadOnlyList<(string Sha, DateTimeOffset At)> before,
        IReadOnlyList<(string Sha, DateTimeOffset At)> after,
        CancellationToken cancellationToken)
    {
        if (after.Count == 0 || before.Count == 0) return (0, true);

        var complete = before.Count <= MaxChurnCommits && after.Count <= MaxChurnCommits;

        var beforeFiles = await FilesInAsync(prefix, before.TakeLast(MaxChurnCommits), cancellationToken)
            .ConfigureAwait(false);

        var afterFiles = await FilesInAsync(prefix, after.Take(MaxChurnCommits), cancellationToken)
            .ConfigureAwait(false);

        afterFiles.IntersectWith(beforeFiles);

        return (afterFiles.Count, complete);
    }

    private async Task<HashSet<string>> FilesInAsync(
        string prefix,
        IEnumerable<(string Sha, DateTimeOffset At)> commits,
        CancellationToken cancellationToken)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);

        foreach (var commit in commits)
        {
            JsonElement response;
            try
            {
                response = await transport.SendAsync(
                    HttpMethod.Get,
                    $"{prefix}/commits/{commit.Sha}",
                    body: null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (GitHubException)
            {
                // A commit that has been garbage-collected after a force push is
                // gone, and one unreadable commit is not a reason to fail the
                // repository. The union is simply smaller.
                continue;
            }

            if (response.ValueKind != JsonValueKind.Object) continue;
            if (!response.TryGetProperty("files", out var rows) || rows.ValueKind != JsonValueKind.Array) continue;

            foreach (var row in rows.EnumerateArray())
            {
                if (String(row, "filename") is { } filename) files.Add(filename);
            }
        }

        return files;
    }

    private async Task<int> CountForcePushesAsync(
        string path,
        DateTimeOffset after,
        CancellationToken cancellationToken)
    {
        try
        {
            var events = await ReadArrayAsync(path, cancellationToken).ConfigureAwait(false);

            return events.Count(item =>
                string.Equals(String(item, "event"), "head_ref_force_pushed", StringComparison.OrdinalIgnoreCase)
                && Timestamp(item, "created_at") is { } at
                && at > after);
        }
        catch (GitHubException)
        {
            // The timeline is the one endpoint here a token can be refused for
            // while the rest work. Losing force-push counts is better than losing
            // the pull request.
            return 0;
        }
    }

    /// <summary>
    /// Closed issues in the window authored by this person, excluding pull
    /// requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>since</c> filters on last update rather than on close, so the window is
    /// applied again here on <c>closed_at</c>. Without that, an issue closed months
    /// ago and commented on yesterday would count as closed this week.
    /// </para>
    /// <para>
    /// <c>state=all</c> rather than <c>closed</c>, and that is what makes the
    /// listing safe to keep. A closed issue is not quite a fact: it can be reopened.
    /// Reopening touches it, so it comes back in the next walk — but only if the
    /// walk asks for open issues too, and when it does the stored row is dropped
    /// rather than left to count as closed forever. The open issues touched since
    /// the last walk are the price, and it is a page at most.
    /// </para>
    /// </remarks>
    private async Task<IssueWalk> ReadIssuesAsync(
        GitHubRepositoryRef repository,
        DateTimeOffset from,
        DateTimeOffset to,
        string author,
        ActivityListing remembered,
        CancellationToken cancellationToken)
    {
        var (stopAt, delta) = WhereToStop(remembered.IssueCoverage, from);

        var known = remembered.Issues.ToDictionary(issue => issue.Number);
        DateTimeOffset? newest = null;
        DateTimeOffset? oldest = null;
        var complete = false;

        for (var page = 1; page <= MaxPages; page++)
        {
            var response = await transport.SendAsync(
                HttpMethod.Get,
                $"repos/{repository.Owner}/{repository.Name}/issues"
                    + $"?state=all&creator={Uri.EscapeDataString(author)}"
                    + $"&since={Uri.EscapeDataString(Rfc3339(stopAt))}"
                    + $"&sort=updated&direction=desc&per_page={PageSize}&page={page}",
                body: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.ValueKind != JsonValueKind.Array)
            {
                complete = true;
                break;
            }

            var rows = response.EnumerateArray().ToList();
            if (rows.Count == 0)
            {
                complete = true;
                break;
            }

            foreach (var row in rows)
            {
                // GitHub's issues endpoint returns pull requests too, marked by the
                // presence of this property.
                if (row.TryGetProperty("pull_request", out _)) continue;

                if (Timestamp(row, "updated_at") is { } seen)
                {
                    if (newest is null || seen > newest) newest = seen;
                    if (oldest is null || seen < oldest) oldest = seen;
                }

                var number = Number(row, "number");

                if (Timestamp(row, "closed_at") is not { } closedAt)
                {
                    // Open now, whatever it was when the last walk saw it.
                    known.Remove(number);
                    continue;
                }

                known[number] = new ListedIssue(
                    number,
                    String(row, "html_url") ?? string.Empty,
                    String(row, "title") ?? string.Empty,
                    closedAt);
            }

            if (rows.Count < PageSize)
            {
                complete = true;
                break;
            }
        }

        var listed = known.Values.OrderByDescending(issue => issue.ClosedAt).ToList();

        var issues = listed
            .Where(issue => issue.ClosedAt >= from && issue.ClosedAt <= to)
            .Select(issue => new GitHubClosedIssue(issue.Number, issue.Url, issue.Title, issue.ClosedAt))
            .ToList();

        return new IssueWalk(
            issues,
            listed,
            CoverageAfter(remembered.IssueCoverage, complete, delta, from, stopAt, newest, oldest));
    }

    private async Task<List<JsonElement>> ReadArrayAsync(string path, CancellationToken cancellationToken)
    {
        var response = await transport
            .SendAsync(HttpMethod.Get, path, body: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return response.ValueKind == JsonValueKind.Array ? [.. response.EnumerateArray()] : [];
    }

    /// <summary>Whether a review state is a verdict rather than a comment.</summary>
    private static bool Verdict(string? state) =>
        string.Equals(state, "APPROVED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(state, "CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase);

    private static bool IsAuthor(JsonElement row, string author) =>
        row.TryGetProperty("user", out var user)
        && user.ValueKind == JsonValueKind.Object
        && string.Equals(String(user, "login"), author, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// When a commit landed. The committer date rather than the author date: a
    /// rebase keeps the author date from before the review and would make every
    /// post-review commit look like it predated the review.
    /// </summary>
    private static DateTimeOffset? CommitInstant(JsonElement element)
    {
        if (!element.TryGetProperty("commit", out var commit) || commit.ValueKind != JsonValueKind.Object) return null;

        foreach (var name in (string[])["committer", "author"])
        {
            if (commit.TryGetProperty(name, out var who)
                && who.ValueKind == JsonValueKind.Object
                && Timestamp(who, "date") is { } date)
            {
                return date;
            }
        }

        return null;
    }

    private static string Rfc3339(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var parsed)
            ? parsed
            : 0;

    private static DateTimeOffset? Timestamp(JsonElement element, string name) =>
        String(element, name) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
}
