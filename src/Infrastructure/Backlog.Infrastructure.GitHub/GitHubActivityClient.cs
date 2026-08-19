using System.Globalization;
using System.Text.Json;

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
/// two calls per repository; each pull request then costs three more, and files
/// re-touched costs one per post-review commit on top. So the per-pull-request work
/// only happens for pull requests that reached the window, and the file inspection
/// is capped — with the cap reported rather than swallowed.
/// </para>
/// </remarks>
public sealed class GitHubActivityClient(IGitHubTransport transport) : IGitHubActivityClient
{
    /// <summary>GitHub caps a list page at 100.</summary>
    private const int PageSize = 100;

    /// <summary>Paging is bounded rather than trusted to terminate.</summary>
    private const int MaxPages = 10;

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

        // Concurrently: the two listings share nothing, and one waiting on the other is
        // a second of a person's time for no reason.
        var pullRequests = ReadPullRequestsAsync(repository, from, to, author, cancellationToken);
        var issues = ReadIssuesAsync(repository, from, to, author, cancellationToken);

        await Task.WhenAll(pullRequests, issues).ConfigureAwait(false);

        return new GitHubRepositoryActivity(
            repository.FullName,
            await pullRequests.ConfigureAwait(false),
            await issues.ConfigureAwait(false));
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
    private async Task<IReadOnlyList<GitHubReviewedPullRequest>> ReadPullRequestsAsync(
        GitHubRepositoryRef repository,
        DateTimeOffset from,
        DateTimeOffset to,
        string author,
        CancellationToken cancellationToken)
    {
        var merged = new List<(int Number, string Url, string Title, DateTimeOffset Created, DateTimeOffset Merged)>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var response = await transport.SendAsync(
                HttpMethod.Get,
                $"repos/{repository.Owner}/{repository.Name}/pulls"
                    + $"?state=closed&sort=updated&direction=desc&per_page={PageSize}&page={page}",
                body: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.ValueKind != JsonValueKind.Array) break;

            var rows = response.EnumerateArray().ToList();
            if (rows.Count == 0) break;

            var pastWindow = false;

            foreach (var row in rows)
            {
                var updated = Timestamp(row, "updated_at");

                // Sorted by updated descending, so once a row was last touched
                // before the window nothing after it can be inside one.
                if (updated is { } stamp && stamp < from)
                {
                    pastWindow = true;
                    break;
                }

                if (Timestamp(row, "merged_at") is not { } mergedAt) continue;
                if (mergedAt < from || mergedAt > to) continue;
                if (!IsAuthor(row, author)) continue;

                merged.Add((
                    Number(row, "number"),
                    String(row, "html_url") ?? string.Empty,
                    String(row, "title") ?? string.Empty,
                    Timestamp(row, "created_at") ?? mergedAt,
                    mergedAt));
            }

            if (pastWindow || rows.Count < PageSize) break;
        }

        var detailed = new List<GitHubReviewedPullRequest>(merged.Count);

        // In batches rather than one at a time, and in batches rather than all at once
        // — see MaxConcurrentPullRequests. Order is preserved because the batches are
        // walked in order and each batch keeps its own.
        foreach (var batch in merged.Chunk(MaxConcurrentPullRequests))
        {
            var read = await Task
                .WhenAll(batch.Select(pullRequest => ReadChurnAsync(repository, pullRequest, cancellationToken)))
                .ConfigureAwait(false);

            detailed.AddRange(read);
        }

        return detailed;
    }

    /// <summary>
    /// The churn detail for one pull request: when it was first reviewed, how many
    /// rounds it took, and what happened after that first review.
    /// </summary>
    private async Task<GitHubReviewedPullRequest> ReadChurnAsync(
        GitHubRepositoryRef repository,
        (int Number, string Url, string Title, DateTimeOffset Created, DateTimeOffset Merged) pullRequest,
        CancellationToken cancellationToken)
    {
        var prefix = $"repos/{repository.Owner}/{repository.Name}";

        var reviews = await ReadArrayAsync(
            $"{prefix}/pulls/{pullRequest.Number}/reviews?per_page={PageSize}",
            cancellationToken).ConfigureAwait(false);

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
            // Two calls saved per unreviewed pull request, and the record says
            // plainly that it had no review rather than that it had no churn.
            return new GitHubReviewedPullRequest(
                pullRequest.Number,
                pullRequest.Url,
                pullRequest.Title,
                pullRequest.Created,
                pullRequest.Merged,
                FirstReviewedAt: null,
                ReviewRounds: 0,
                ChangesRequested: changesRequested,
                CommitsAfterFirstReview: 0,
                ForcePushesAfterFirstReview: 0,
                FilesRetouched: 0,
                ChurnComplete: true);
        }

        var commits = await ReadArrayAsync(
            $"{prefix}/pulls/{pullRequest.Number}/commits?per_page={PageSize}",
            cancellationToken).ConfigureAwait(false);

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
            pullRequest.Created,
            pullRequest.Merged,
            firstReviewedAt,
            ReviewRounds: verdicts.Count,
            ChangesRequested: changesRequested,
            CommitsAfterFirstReview: after.Count,
            ForcePushesAfterFirstReview: forcePushes,
            FilesRetouched: retouched,
            ChurnComplete: complete);
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
    /// <c>since</c> filters on last update rather than on close, so the window is
    /// applied again here on <c>closed_at</c>. Without that, an issue closed months
    /// ago and commented on yesterday would count as closed this week.
    /// </remarks>
    private async Task<IReadOnlyList<GitHubClosedIssue>> ReadIssuesAsync(
        GitHubRepositoryRef repository,
        DateTimeOffset from,
        DateTimeOffset to,
        string author,
        CancellationToken cancellationToken)
    {
        var issues = new List<GitHubClosedIssue>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var response = await transport.SendAsync(
                HttpMethod.Get,
                $"repos/{repository.Owner}/{repository.Name}/issues"
                    + $"?state=closed&creator={Uri.EscapeDataString(author)}"
                    + $"&since={Uri.EscapeDataString(Rfc3339(from))}"
                    + $"&sort=updated&direction=desc&per_page={PageSize}&page={page}",
                body: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.ValueKind != JsonValueKind.Array) break;

            var rows = response.EnumerateArray().ToList();
            if (rows.Count == 0) break;

            foreach (var row in rows)
            {
                // GitHub's issues endpoint returns pull requests too, marked by the
                // presence of this property.
                if (row.TryGetProperty("pull_request", out _)) continue;

                if (Timestamp(row, "closed_at") is not { } closedAt) continue;
                if (closedAt < from || closedAt > to) continue;

                issues.Add(new GitHubClosedIssue(
                    Number(row, "number"),
                    String(row, "html_url") ?? string.Empty,
                    String(row, "title") ?? string.Empty,
                    closedAt));
            }

            if (rows.Count < PageSize) break;
        }

        return issues;
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
