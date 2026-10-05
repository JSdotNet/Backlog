using System.Text.Json;
using static Backlog.Infrastructure.IsoTimestamps;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// The operations the app actually performs against GitHub: put an entry on the
/// board, and find out what has happened to it since.
/// </summary>
public interface IGitHubClient
{
    Task<GitHubIssue> CreateIssueAsync(
        GitHubRepositoryRef repository,
        string title,
        string? body,
        IEnumerable<string>? labels = null,
        CancellationToken cancellationToken = default);

    Task<GitHubIssueSnapshot> GetIssueAsync(
        GitHubRepositoryRef repository,
        int number,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one pull request by number — what an entry's recorded pull
    /// request is asked, so its link can say whether it merged.
    /// <para>
    /// A default body because every test double in the suite implements this
    /// interface and none of them is about pull requests: one that is not asked
    /// answers like a GitHub that cannot find it, which a caller already has to
    /// treat as "not read".
    /// </para></summary>
    Task<GitHubPullRequest> GetPullRequestAsync(
        GitHubRepositoryRef repository,
        int number,
        CancellationToken cancellationToken = default) =>
        Task.FromException<GitHubPullRequest>(new GitHubException("This GitHub client cannot read a pull request."));

    /// <summary>
    /// Reads one pull request's state, check roll-up and auto-merge request, and
    /// what the merge acts need to act on it — one GraphQL query. See
    /// <see cref="GitHubPullRequestStatus"/>.
    /// <para>
    /// A default body for the reason <see cref="GetPullRequestAsync"/> has one: a
    /// test double that is not about pull requests answers like a GitHub that
    /// cannot find it, which the caller already treats as "not read".
    /// </para>
    /// </summary>
    Task<GitHubPullRequestStatus> GetPullRequestStatusAsync(
        GitHubRepositoryRef repository,
        int number,
        CancellationToken cancellationToken = default) =>
        Task.FromException<GitHubPullRequestStatus>(new GitHubException("This GitHub client cannot read a pull request's status."));

    /// <summary>
    /// Asks GitHub to merge the pull request by itself once its requirements are
    /// met — GitHub's native auto-merge. GitHub does the waiting; nothing in this
    /// app polls or merges on a timer.
    /// </summary>
    /// <param name="pullRequestId">The pull request's GraphQL node id, from
    /// <see cref="GetPullRequestStatusAsync"/>.</param>
    Task EnableAutoMergeAsync(
        GitHubRepositoryRef repository,
        string pullRequestId,
        GitHubMergeMethod method,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new GitHubException("This GitHub client cannot turn on auto-merge."));

    /// <summary>Withdraws a pending auto-merge request.</summary>
    Task DisableAutoMergeAsync(
        GitHubRepositoryRef repository,
        string pullRequestId,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new GitHubException("This GitHub client cannot cancel auto-merge."));

    /// <summary>Merges the pull request now. The act for a pull request GitHub
    /// would refuse to queue because it is already mergeable.</summary>
    Task MergePullRequestAsync(
        GitHubRepositoryRef repository,
        string pullRequestId,
        GitHubMergeMethod method,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new GitHubException("This GitHub client cannot merge a pull request."));

    /// <summary>
    /// The repository's open pull requests, most recently updated first — up to fifty,
    /// with what the pull requests list shows about each and what its acts need. One
    /// GraphQL query. See <see cref="GitHubOpenPullRequest"/>.
    /// <para>
    /// A default body for the reason <see cref="GetPullRequestAsync"/> has one: a test
    /// double that is not about pull requests answers like a GitHub that refused, which
    /// the list already reports per repository.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<GitHubOpenPullRequest>> ListOpenPullRequestsAsync(
        GitHubRepositoryRef repository,
        CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<GitHubOpenPullRequest>>(new GitHubException("This GitHub client cannot list pull requests."));

    /// <summary>
    /// The repository's pull requests merged at or after <paramref name="mergedSince"/>,
    /// with when and by whom each was merged, in the order GitHub lists them — most
    /// recently updated first, because GitHub cannot order by merge time. Paged until the
    /// window is passed or a page cap is reached, which the answer says.
    /// A default body for the reason <see cref="ListOpenPullRequestsAsync"/> has one.
    /// </summary>
    Task<GitHubMergedPullRequestRead> ListMergedPullRequestsAsync(
        GitHubRepositoryRef repository,
        DateTimeOffset mergedSince,
        CancellationToken cancellationToken = default) =>
        Task.FromException<GitHubMergedPullRequestRead>(new GitHubException("This GitHub client cannot list merged pull requests."));

    /// <summary>
    /// The repository's issues through the issue search: every open one, plus, when
    /// <paramref name="closedSince"/> is given, every one closed at or after it. Pull
    /// requests are left out. Paged until search has nothing more to give; an answer
    /// search cut short says so in <see cref="GitHubIssueSearchRead.Truncated"/>.
    /// <para>
    /// A default body for the reason <see cref="GetPullRequestAsync"/> has one: a test
    /// double that is not about linked tasks answers like a GitHub that refused.
    /// </para>
    /// </summary>
    Task<GitHubIssueSearchRead> SearchIssuesAsync(
        GitHubRepositoryRef repository,
        DateTimeOffset? closedSince,
        CancellationToken cancellationToken = default) =>
        Task.FromException<GitHubIssueSearchRead>(new GitHubException("This GitHub client cannot search issues."));

    /// <summary>Takes a draft out of draft — GitHub's "Ready for review".</summary>
    /// <param name="pullRequestId">The pull request's GraphQL node id, from
    /// <see cref="ListOpenPullRequestsAsync"/>.</param>
    Task MarkReadyForReviewAsync(
        GitHubRepositoryRef repository,
        string pullRequestId,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new GitHubException("This GitHub client cannot mark a pull request ready for review."));

    /// <summary>
    /// Merges the base branch into the pull request's head — GitHub's "Update branch".
    /// GitHub does the merge on its side and answers before it has finished, so the
    /// caller reads the pull request again rather than trusting the answer.
    /// </summary>
    /// <param name="expectedHeadSha">The head commit the caller read. GitHub refuses
    /// when the branch has moved since, rather than merging into commits nobody here
    /// has seen. Null updates whatever the head is.</param>
    Task UpdateBranchAsync(
        GitHubRepositoryRef repository,
        int number,
        string? expectedHeadSha,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new GitHubException("This GitHub client cannot update a pull request's branch."));

    /// <summary>Commits <paramref name="content"/> to <paramref name="path"/> on
    /// <paramref name="branch"/>, creating the branch off the repository's default
    /// branch first if it does not already exist, and returns the raw URL the
    /// committed file can be linked from (an issue body, for one — GitHub's
    /// markdown sanitizer strips embedded <c>data:</c> images, so a real file
    /// committed to the repository is what makes an attached screenshot actually
    /// render).</summary>
    Task<GitHubUploadedFile> UploadFileAsync(
        GitHubRepositoryRef repository,
        string path,
        string branch,
        byte[] content,
        string commitMessage,
        CancellationToken cancellationToken = default);

    /// <summary>Puts <paramref name="content"/> at <paramref name="path"/> on the
    /// repository's default branch, replacing what is there. The counterpart of
    /// <see cref="UploadFileAsync"/> for a file that is written again and again
    /// rather than once: that one creates and is refused a second time, because
    /// the Contents API wants the blob it is replacing named; this one asks for
    /// it first. A file GitHub already holds with exactly this content is left
    /// alone — the answer says so — because a commit that changes nothing is
    /// history nobody asked for.</summary>
    Task<GitHubCommittedFile> CommitFileAsync(
        GitHubRepositoryRef repository,
        string path,
        byte[] content,
        string commitMessage,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IGitHubClient"/> over whichever transport can authenticate. The
/// resource paths are identical either way, so the choice of transport never
/// leaks past this class.
/// </summary>
public sealed class GitHubClient(IGitHubTransport transport) : IGitHubClient
{
    public async Task<GitHubIssue> CreateIssueAsync(
        GitHubRepositoryRef repository,
        string title,
        string? body,
        IEnumerable<string>? labels = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new GitHubException("An issue needs a title — give the entry one first.");
        }

        var labelList = labels?.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct().ToList();

        var payload = new Dictionary<string, object?>
        {
            ["title"] = title.Trim(),
            ["body"] = string.IsNullOrWhiteSpace(body) ? null : body
        };

        if (labelList is { Count: > 0 }) payload["labels"] = labelList;

        var response = await transport.SendAsync(
            HttpMethod.Post,
            $"repos/{repository.Owner}/{repository.Name}/issues",
            payload,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return ReadIssue(response);
    }

    public async Task<GitHubIssueSnapshot> GetIssueAsync(
        GitHubRepositoryRef repository,
        int number,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var issueResponse = await transport.SendAsync(
            HttpMethod.Get,
            $"repos/{repository.Owner}/{repository.Name}/issues/{number}",
            body: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var issue = ReadIssue(issueResponse);

        IReadOnlyList<GitHubPullRequest> pullRequests;
        try
        {
            var timeline = await transport.SendAsync(
                HttpMethod.Get,
                $"repos/{repository.Owner}/{repository.Name}/issues/{number}/timeline?per_page=100",
                body: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            pullRequests = ReadLinkedPullRequests(timeline);
        }
        catch (GitHubException)
        {
            // The issue's own state is the thing that matters; not being able to
            // read its timeline should not turn the whole refresh into a failure.
            pullRequests = [];
        }

        return new GitHubIssueSnapshot(issue, pullRequests, DateTimeOffset.UtcNow);
    }

    public async Task<GitHubPullRequest> GetPullRequestAsync(
        GitHubRepositoryRef repository,
        int number,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var response = await transport.SendAsync(
            HttpMethod.Get,
            $"repos/{repository.Owner}/{repository.Name}/pulls/{number}",
            body: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return ReadPullRequest(response, repository.FullName);
    }

    /// <summary>
    /// The status query. The repository's merge settings ride along with the pull
    /// request because the method an auto-merge request is made with has to be one
    /// the repository allows, and asking in the same round trip keeps "read, then
    /// act" to one read.
    /// <para>
    /// The check roll-up is the head commit's — <c>commits(last: 1)</c> — which is
    /// the commit GitHub gates the merge on.
    /// </para>
    /// </summary>
    private const string StatusQuery = """
        query($owner: String!, $name: String!, $number: Int!) {
          repository(owner: $owner, name: $name) {
            mergeCommitAllowed
            squashMergeAllowed
            rebaseMergeAllowed
            pullRequest(number: $number) {
              id
              number
              state
              isDraft
              mergeStateStatus
              autoMergeRequest { enabledAt }
              commits(last: 1) { nodes { commit { statusCheckRollup { state } } } }
            }
          }
        }
        """;

    private const string EnableAutoMergeMutation = """
        mutation($id: ID!, $method: PullRequestMergeMethod!) {
          enablePullRequestAutoMerge(input: { pullRequestId: $id, mergeMethod: $method }) { clientMutationId }
        }
        """;

    private const string DisableAutoMergeMutation = """
        mutation($id: ID!) {
          disablePullRequestAutoMerge(input: { pullRequestId: $id }) { clientMutationId }
        }
        """;

    private const string MergeMutation = """
        mutation($id: ID!, $method: PullRequestMergeMethod!) {
          mergePullRequest(input: { pullRequestId: $id, mergeMethod: $method }) { clientMutationId }
        }
        """;

    private const string ReadyForReviewMutation = """
        mutation($id: ID!) {
          markPullRequestReadyForReview(input: { pullRequestId: $id }) { clientMutationId }
        }
        """;

    /// <summary>
    /// The list query: the status query's fields for every open pull request at once,
    /// plus what a list shows that a single link does not — the branches, the author,
    /// and the merge state's other answers. The merge settings ride along for the
    /// reason they do on <see cref="StatusQuery"/>.
    /// <para>
    /// Fifty, most recently updated first, and no paging. A repository with more open
    /// pull requests than that has a queue nobody reads row by row, and the ones at
    /// the top are the ones that moved; a second page would be cost with no reader.
    /// </para>
    /// </summary>
    private const string OpenPullRequestsQuery = """
        query($owner: String!, $name: String!) {
          repository(owner: $owner, name: $name) {
            mergeCommitAllowed
            squashMergeAllowed
            rebaseMergeAllowed
            pullRequests(states: OPEN, first: 50, orderBy: { field: UPDATED_AT, direction: DESC }) {
              nodes {
                id
                number
                title
                url
                isDraft
                headRefName
                headRefOid
                baseRefName
                mergeStateStatus
                mergeable
                viewerDidAuthor
                author { login }
                autoMergeRequest { enabledAt }
                updatedAt
                commits(last: 1) { nodes { commit { statusCheckRollup { state } } } }
              }
            }
          }
        }
        """;

    /// <summary>How many merged pull requests one page of the merged query asks for —
    /// GitHub's maximum for a connection. Sent as the query's <c>$first</c>, so the
    /// query and this number cannot disagree.</summary>
    public const int MergedPageSize = 100;

    /// <summary>How many pages one merged read follows at most: five hundred pull
    /// requests, a fortnight of a repository merging thirty-five a day. A bound so one
    /// very busy repository cannot hold the whole view on a long walk of GraphQL calls;
    /// reaching it inside the window is reported, not hidden.</summary>
    public const int MergedPageCap = 5;

    /// <summary>
    /// The merged view's query, one page of it. Ordered by update because GitHub's
    /// <c>IssueOrderField</c> has no merge time; a merge updates the pull request, so a
    /// pull request merged inside the window was updated inside it too, and the walk
    /// can stop at the first page that reaches back past the window.
    /// </summary>
    private const string MergedPullRequestsQuery = """
        query($owner: String!, $name: String!, $first: Int!, $after: String) {
          repository(owner: $owner, name: $name) {
            pullRequests(states: MERGED, first: $first, after: $after, orderBy: { field: UPDATED_AT, direction: DESC }) {
              pageInfo { hasNextPage endCursor }
              nodes {
                updatedAt
                id
                number
                title
                url
                headRefName
                baseRefName
                author { login }
                viewerDidAuthor
                mergedAt
                mergedBy { login }
              }
            }
          }
        }
        """;

    public async Task<GitHubPullRequestStatus> GetPullRequestStatusAsync(
        GitHubRepositoryRef repository,
        int number,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var data = await GitHubGraphQl.SendAsync(
            transport,
            repository,
            StatusQuery,
            new Dictionary<string, object?>
            {
                ["owner"] = repository.Owner,
                ["name"] = repository.Name,
                ["number"] = number
            },
            cancellationToken,
            tolerate: OnlyTheCheckRollupWasRefused).ConfigureAwait(false);

        return ReadPullRequestStatus(data, repository.FullName, number);
    }

    /// <summary>
    /// The one partial status answer that is still a status: every error points
    /// inside <c>statusCheckRollup</c>, and the pull request itself came back. A
    /// fine-grained token without access to checks is answered exactly this way —
    /// FORBIDDEN on the roll-up, everything else intact — and the state, the
    /// auto-merge request and the merge act do not depend on the one field it cannot
    /// see. The roll-up comes back null, which <see cref="ReadChecks"/> already reads
    /// as no checks.
    /// </summary>
    private static bool OnlyTheCheckRollupWasRefused(IReadOnlyList<JsonElement> errors, JsonElement data) =>
        errors.All(error => GitHubGraphQl.PathPassesThrough(error, "statusCheckRollup"))
        && data.TryGetProperty("repository", out var repository)
        && repository.ValueKind == JsonValueKind.Object
        && repository.TryGetProperty("pullRequest", out var pull)
        && pull.ValueKind == JsonValueKind.Object;

    public Task EnableAutoMergeAsync(
        GitHubRepositoryRef repository,
        string pullRequestId,
        GitHubMergeMethod method,
        CancellationToken cancellationToken = default) =>
        MutateAsync(repository, EnableAutoMergeMutation, pullRequestId, method, cancellationToken);

    public Task DisableAutoMergeAsync(
        GitHubRepositoryRef repository,
        string pullRequestId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(repository, DisableAutoMergeMutation, pullRequestId, method: null, cancellationToken);

    public Task MergePullRequestAsync(
        GitHubRepositoryRef repository,
        string pullRequestId,
        GitHubMergeMethod method,
        CancellationToken cancellationToken = default) =>
        MutateAsync(repository, MergeMutation, pullRequestId, method, cancellationToken);

    public async Task<IReadOnlyList<GitHubOpenPullRequest>> ListOpenPullRequestsAsync(
        GitHubRepositoryRef repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var data = await GitHubGraphQl.SendAsync(
            transport,
            repository,
            OpenPullRequestsQuery,
            new Dictionary<string, object?>
            {
                ["owner"] = repository.Owner,
                ["name"] = repository.Name
            },
            cancellationToken,
            tolerate: OnlyTheListsCheckRollupsWereRefused).ConfigureAwait(false);

        return ReadOpenPullRequests(data, repository.FullName);
    }

    /// <summary>
    /// Every page sent the way the open list is, through the repository's own GraphQL
    /// path, so the account bound to the repository is the one that answers. The walk
    /// stops when GitHub has no next page, when a page's oldest update is before
    /// <paramref name="mergedSince"/> — nothing after it in the order can have been
    /// merged inside the window — or at <see cref="MergedPageCap"/>, which is reported
    /// as truncation only if the window had not been passed yet.
    /// </summary>
    public async Task<GitHubMergedPullRequestRead> ListMergedPullRequestsAsync(
        GitHubRepositoryRef repository,
        DateTimeOffset mergedSince,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var pulls = new List<GitHubMergedPullRequest>();
        var seen = new HashSet<int>();
        string? after = null;

        for (var page = 1; ; page++)
        {
            var data = await GitHubGraphQl.SendAsync(
                transport,
                repository,
                MergedPullRequestsQuery,
                new Dictionary<string, object?>
                {
                    ["owner"] = repository.Owner,
                    ["name"] = repository.Name,
                    ["first"] = MergedPageSize,
                    ["after"] = after
                },
                cancellationToken).ConfigureAwait(false);

            var read = ReadMergedPullRequests(data, repository.FullName);
            // A pull request updated between two pages moves up the order and can come
            // back on the next one; the first sighting stands, because the table keys
            // its rows by repository and number and a second row would collide.
            pulls.AddRange(read.PullRequests.Where(pull => pull.MergedAt >= mergedSince && seen.Add(pull.Number)));

            var passedTheWindow = read.OldestUpdate is { } oldest && oldest < mergedSince;
            if (!read.HasNextPage || read.EndCursor is null || passedTheWindow)
            {
                return new GitHubMergedPullRequestRead(pulls, Truncated: false);
            }

            if (page >= MergedPageCap)
            {
                return new GitHubMergedPullRequestRead(pulls, Truncated: true);
            }

            after = read.EndCursor;
        }
    }

    /// <summary>How many issues one search page asks for — the search API's
    /// maximum.</summary>
    public const int SearchPageSize = 100;

    /// <summary>How many pages one search query follows at most. Search hands back
    /// the first thousand matches and no more, so a tenth page is the last one that
    /// can hold anything.</summary>
    public const int SearchPageCap = 10;

    /// <summary>
    /// The open query, then the closed one when there is a since. Both through
    /// <c>search/issues</c>, whose <c>repo:</c> qualifier is what
    /// <see cref="GitHubSettings.AccountForPath"/> routes to the repository's
    /// account. An issue can come back from both — it closed between the two
    /// queries, or search has not re-indexed it yet — and the later sighting
    /// replaces the earlier one, so the closed query's answer stands.
    /// </summary>
    public async Task<GitHubIssueSearchRead> SearchIssuesAsync(
        GitHubRepositoryRef repository,
        DateTimeOffset? closedSince,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var issues = new OrderedDictionary<string, GitHubSearchedIssue>(StringComparer.Ordinal);
        var truncated = await SearchAsync(repository, closedSince: null, issues, cancellationToken).ConfigureAwait(false);

        if (closedSince is { } since)
        {
            truncated |= await SearchAsync(repository, since, issues, cancellationToken).ConfigureAwait(false);
        }

        return new GitHubIssueSearchRead([.. issues.Values], truncated);
    }

    /// <summary>One query, every page of it, into <paramref name="issues"/> by node
    /// id. Answers whether search held anything back.</summary>
    private async Task<bool> SearchAsync(
        GitHubRepositoryRef repository,
        DateTimeOffset? closedSince,
        OrderedDictionary<string, GitHubSearchedIssue> issues,
        CancellationToken cancellationToken)
    {
        var read = 0;

        for (var page = 1; ; page++)
        {
            var response = await transport.SendAsync(
                HttpMethod.Get,
                SearchIssuesPath(repository, closedSince, page),
                body: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var answer = ReadSearchedIssues(response);
            read += answer.Rows;
            foreach (var issue in answer.Issues) issues[issue.NodeId] = issue;

            if (answer.Incomplete) return true;

            // Fewer rows than a page holds is the last page, whatever the total
            // says; a total the walk has reached is the end too.
            if (answer.Rows < SearchPageSize || read >= answer.TotalCount) return false;

            if (page >= SearchPageCap) return true;
        }
    }

    /// <summary>
    /// The search path for one page. Qualifiers are joined with <c>+</c>, the way
    /// GitHub's own query strings carry a space; the closed qualifier's value is
    /// escaped because <c>&gt;=</c> and the time's colons would otherwise be read as
    /// part of the URL. Oldest first, so an issue opened while the walk runs lands on
    /// a later page instead of pushing an unread one onto a page already read.
    /// </summary>
    internal static string SearchIssuesPath(GitHubRepositoryRef repository, DateTimeOffset? closedSince, int page)
    {
        var state = closedSince is { } since
            ? $"is:closed+closed:{Uri.EscapeDataString($">={Rfc3339(since)}")}"
            : "is:open";

        return $"search/issues?q=is:issue+repo:{repository.Owner}/{repository.Name}+{state}"
            + $"&sort=created&order=asc&per_page={SearchPageSize}&page={page}";
    }

    /// <summary>
    /// One search page. A row carrying a <c>pull_request</c> object is a pull
    /// request and is left out, as is one without a node id or a number; both still
    /// count as rows read, because paging is about where search is, not about what
    /// was kept.
    /// </summary>
    internal static SearchPage ReadSearchedIssues(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object)
        {
            throw new GitHubException("GitHub did not return search results.");
        }

        var total = response.TryGetProperty("total_count", out var count) && count.TryGetInt32(out var value) ? value : 0;
        var incomplete = response.TryGetProperty("incomplete_results", out var partial) && partial.ValueKind == JsonValueKind.True;

        if (!response.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return new SearchPage([], 0, total, incomplete);
        }

        var issues = new List<GitHubSearchedIssue>();
        var rows = 0;

        foreach (var item in items.EnumerateArray())
        {
            rows++;
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (item.TryGetProperty("pull_request", out var pull) && pull.ValueKind == JsonValueKind.Object) continue;

            var nodeId = String(item, "node_id");
            var number = item.TryGetProperty("number", out var n) && n.TryGetInt32(out var parsed) ? parsed : 0;
            if (string.IsNullOrWhiteSpace(nodeId) || number == 0) continue;

            issues.Add(new GitHubSearchedIssue(
                nodeId,
                number,
                String(item, "html_url") ?? string.Empty,
                String(item, "title") ?? string.Empty,
                String(item, "body") ?? string.Empty,
                IsOpen: !string.Equals(String(item, "state"), "closed", StringComparison.OrdinalIgnoreCase),
                StateReason: String(item, "state_reason"),
                AssigneeLogin: Login(item, "assignee"),
                UpdatedAt: Timestamp(item, "updated_at") ?? DateTimeOffset.MinValue,
                Labels: ReadLabelNames(item)));
        }

        return new SearchPage(issues, rows, total, incomplete);
    }

    /// <summary>One search page: the issues kept, how many rows it held, the total
    /// search reported, and whether search said it gave up early.</summary>
    internal sealed record SearchPage(IReadOnlyList<GitHubSearchedIssue> Issues, int Rows, int TotalCount, bool Incomplete);

    private static IReadOnlyList<string> ReadLabelNames(JsonElement item)
    {
        if (!item.TryGetProperty("labels", out var labels) || labels.ValueKind != JsonValueKind.Array) return [];

        return [.. labels.EnumerateArray()
            .Select(label => label.ValueKind == JsonValueKind.Object ? String(label, "name") : null)
            .OfType<string>()
            .Where(name => name.Length > 0)];
    }

    /// <summary>The list's version of <see cref="OnlyTheCheckRollupWasRefused"/>: the
    /// same refusal, once per pull request, and the same reason to read around it —
    /// a token that cannot see checks can still see which pull requests are open.</summary>
    private static bool OnlyTheListsCheckRollupsWereRefused(IReadOnlyList<JsonElement> errors, JsonElement data) =>
        errors.All(error => GitHubGraphQl.PathPassesThrough(error, "statusCheckRollup"))
        && data.TryGetProperty("repository", out var repository)
        && repository.ValueKind == JsonValueKind.Object;

    public Task MarkReadyForReviewAsync(
        GitHubRepositoryRef repository,
        string pullRequestId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(repository, ReadyForReviewMutation, pullRequestId, method: null, cancellationToken);

    /// <summary>
    /// REST rather than GraphQL, because GraphQL's <c>updatePullRequestBranch</c> takes
    /// the same inputs and nothing more, while this endpoint is the one GitHub documents
    /// <c>expected_head_sha</c> on and the one its own button calls.
    /// </summary>
    public async Task UpdateBranchAsync(
        GitHubRepositoryRef repository,
        int number,
        string? expectedHeadSha,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        // An empty object rather than no body when there is no head to name: the
        // endpoint is a PUT, and the CLI transport sends a body only when given one.
        var payload = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(expectedHeadSha)) payload["expected_head_sha"] = expectedHeadSha;

        await transport.SendAsync(
            HttpMethod.Put,
            $"repos/{repository.Owner}/{repository.Name}/pulls/{number}/update-branch",
            payload,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One of the pull request mutations. Nothing in their payloads is read:
    /// success is the absence of an <c>errors</c> entry, and the caller re-reads the
    /// status to see what it did.</summary>
    private async Task MutateAsync(
        GitHubRepositoryRef repository,
        string mutation,
        string pullRequestId,
        GitHubMergeMethod? method,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (string.IsNullOrWhiteSpace(pullRequestId))
        {
            throw new GitHubException("A pull request has to be read before it can be acted on.");
        }

        var variables = new Dictionary<string, object?> { ["id"] = pullRequestId };
        if (method is { } chosen) variables["method"] = MergeMethodName(chosen);

        await GitHubGraphQl.SendAsync(transport, repository, mutation, variables, cancellationToken).ConfigureAwait(false);
    }

    private static string MergeMethodName(GitHubMergeMethod method) => method switch
    {
        GitHubMergeMethod.Squash => "SQUASH",
        GitHubMergeMethod.Rebase => "REBASE",
        _ => "MERGE"
    };

    /// <summary>The status query's <c>data</c>, as the record the merge controls
    /// read. A missing pull request is a not-found, the same answer the REST read
    /// gives for one.</summary>
    internal static GitHubPullRequestStatus ReadPullRequestStatus(JsonElement data, string repositoryFullName, int number)
    {
        if (!data.TryGetProperty("repository", out var repository) || repository.ValueKind != JsonValueKind.Object
            || !repository.TryGetProperty("pullRequest", out var pull) || pull.ValueKind != JsonValueKind.Object)
        {
            throw new GitHubException($"GitHub couldn't find pull request #{number} in {repositoryFullName}.")
            {
                Status = System.Net.HttpStatusCode.NotFound
            };
        }

        var nodeId = String(pull, "id");
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            throw new GitHubException("GitHub returned a pull request without an id.");
        }

        var state = String(pull, "state") switch
        {
            "MERGED" => GitHubItemState.Merged,
            "CLOSED" => GitHubItemState.Closed,
            _ when pull.TryGetProperty("isDraft", out var draft) && draft.ValueKind == JsonValueKind.True => GitHubItemState.Draft,
            _ => GitHubItemState.Open
        };

        return new GitHubPullRequestStatus(
            pull.TryGetProperty("number", out var n) && n.TryGetInt32(out var value) ? value : number,
            repositoryFullName,
            nodeId,
            state,
            ReadChecks(pull),
            pull.TryGetProperty("autoMergeRequest", out var autoMerge) && autoMerge.ValueKind == JsonValueKind.Object,
            String(pull, "mergeStateStatus") is { } mergeState && MergeableNow.Contains(mergeState),
            PreferredMergeMethod(repository));
    }

    /// <summary>
    /// The list query's <c>data</c>, as the records the pull requests list reads. A
    /// missing repository is a not-found, the answer every other read gives for one;
    /// a node without an id or a number is left out rather than drawn as a row no act
    /// could name.
    /// </summary>
    internal static IReadOnlyList<GitHubOpenPullRequest> ReadOpenPullRequests(JsonElement data, string repositoryFullName)
    {
        if (!data.TryGetProperty("repository", out var repository) || repository.ValueKind != JsonValueKind.Object)
        {
            throw new GitHubException($"GitHub couldn't find {repositoryFullName}.")
            {
                Status = System.Net.HttpStatusCode.NotFound
            };
        }

        if (!repository.TryGetProperty("pullRequests", out var connection) || connection.ValueKind != JsonValueKind.Object
            || !connection.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var method = PreferredMergeMethod(repository);
        var pulls = new List<GitHubOpenPullRequest>();

        foreach (var pull in nodes.EnumerateArray())
        {
            if (pull.ValueKind != JsonValueKind.Object) continue;

            var nodeId = String(pull, "id");
            var number = pull.TryGetProperty("number", out var n) && n.TryGetInt32(out var value) ? value : 0;
            if (string.IsNullOrWhiteSpace(nodeId) || number == 0) continue;

            var mergeState = String(pull, "mergeStateStatus");
            var author = pull.TryGetProperty("author", out var who) && who.ValueKind == JsonValueKind.Object
                ? String(who, "login")
                : null;

            pulls.Add(new GitHubOpenPullRequest(
                number,
                String(pull, "url") ?? $"https://github.com/{repositoryFullName}/pull/{number}",
                String(pull, "title") ?? string.Empty,
                repositoryFullName,
                nodeId,
                IsDraft: pull.TryGetProperty("isDraft", out var draft) && draft.ValueKind == JsonValueKind.True,
                HeadRefName: String(pull, "headRefName") ?? string.Empty,
                HeadSha: String(pull, "headRefOid"),
                BaseRefName: String(pull, "baseRefName") ?? string.Empty,
                AuthorLogin: author,
                ViewerDidAuthor: pull.TryGetProperty("viewerDidAuthor", out var mine) && mine.ValueKind == JsonValueKind.True,
                Checks: ReadChecks(pull),
                AutoMergeEnabled: pull.TryGetProperty("autoMergeRequest", out var autoMerge) && autoMerge.ValueKind == JsonValueKind.Object,
                MergeReady: mergeState is not null && MergeableNow.Contains(mergeState),
                IsBehind: string.Equals(mergeState, "BEHIND", StringComparison.Ordinal),
                HasConflicts: string.Equals(mergeState, "DIRTY", StringComparison.Ordinal)
                    || string.Equals(String(pull, "mergeable"), "CONFLICTING", StringComparison.Ordinal),
                MergeStateStatus: mergeState,
                PreferredMergeMethod: method,
                UpdatedAt: Timestamp(pull, "updatedAt")));
        }

        return pulls;
    }

    /// <summary>
    /// The merged query's <c>data</c>, as the records the merged view reads. A missing
    /// repository is a not-found, as for the open list; a node without a number or a
    /// merge time is left out, because the view is windowed and ordered by that time
    /// and a row without one would belong nowhere in it. The page's oldest update counts
    /// every node, left out or not: it is where the page reaches back to.
    /// </summary>
    internal static MergedPage ReadMergedPullRequests(JsonElement data, string repositoryFullName)
    {
        if (!data.TryGetProperty("repository", out var repository) || repository.ValueKind != JsonValueKind.Object)
        {
            throw new GitHubException($"GitHub couldn't find {repositoryFullName}.")
            {
                Status = System.Net.HttpStatusCode.NotFound
            };
        }

        if (!repository.TryGetProperty("pullRequests", out var connection) || connection.ValueKind != JsonValueKind.Object
            || !connection.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return new MergedPage([], null, false, null);
        }

        connection.TryGetProperty("pageInfo", out var pageInfo);
        var hasNextPage = pageInfo.ValueKind == JsonValueKind.Object
            && pageInfo.TryGetProperty("hasNextPage", out var next) && next.ValueKind == JsonValueKind.True;
        var endCursor = pageInfo.ValueKind == JsonValueKind.Object ? String(pageInfo, "endCursor") : null;

        var pulls = new List<GitHubMergedPullRequest>();
        DateTimeOffset? oldestUpdate = null;

        foreach (var pull in nodes.EnumerateArray())
        {
            if (pull.ValueKind != JsonValueKind.Object) continue;

            if (Timestamp(pull, "updatedAt") is { } updated && (oldestUpdate is null || updated < oldestUpdate))
            {
                oldestUpdate = updated;
            }

            var number = pull.TryGetProperty("number", out var n) && n.TryGetInt32(out var value) ? value : 0;
            if (number == 0 || Timestamp(pull, "mergedAt") is not { } mergedAt) continue;

            pulls.Add(new GitHubMergedPullRequest(
                number,
                String(pull, "url") ?? $"https://github.com/{repositoryFullName}/pull/{number}",
                String(pull, "title") ?? string.Empty,
                repositoryFullName,
                HeadRefName: String(pull, "headRefName") ?? string.Empty,
                BaseRefName: String(pull, "baseRefName") ?? string.Empty,
                AuthorLogin: Login(pull, "author"),
                ViewerDidAuthor: pull.TryGetProperty("viewerDidAuthor", out var mine) && mine.ValueKind == JsonValueKind.True,
                MergedAt: mergedAt,
                MergedByLogin: Login(pull, "mergedBy")));
        }

        return new MergedPage(pulls, oldestUpdate, hasNextPage, endCursor);
    }

    /// <summary>One page of the merged query: its rows, how far back it reached, and
    /// where the next page starts.</summary>
    internal sealed record MergedPage(
        IReadOnlyList<GitHubMergedPullRequest> PullRequests,
        DateTimeOffset? OldestUpdate,
        bool HasNextPage,
        string? EndCursor);

    /// <summary>An actor's login, or null where GitHub answered with no actor — a
    /// deleted account comes back as <c>null</c>.</summary>
    private static string? Login(JsonElement element, string name) =>
        element.TryGetProperty(name, out var actor) && actor.ValueKind == JsonValueKind.Object
            ? String(actor, "login")
            : null;

    /// <summary>
    /// The merge states in which the pull request can merge right now, and in which
    /// GitHub therefore refuses to queue auto-merge: CLEAN; UNSTABLE, where only
    /// checks that are not required are failing; and HAS_HOOKS, clean with pre-receive
    /// hooks to run. <c>gh pr merge --auto</c> draws the same line and merges
    /// directly for these three.
    /// </summary>
    private static readonly HashSet<string> MergeableNow = new(StringComparer.Ordinal) { "CLEAN", "UNSTABLE", "HAS_HOOKS" };

    /// <summary>The head commit's roll-up, if it has one. No commit, or a commit no
    /// check ever reported on, is <see cref="GitHubCheckState.None"/>.</summary>
    private static GitHubCheckState ReadChecks(JsonElement pull)
    {
        if (!pull.TryGetProperty("commits", out var commits) || commits.ValueKind != JsonValueKind.Object) return GitHubCheckState.None;
        if (!commits.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array) return GitHubCheckState.None;

        var head = nodes.EnumerateArray().LastOrDefault();
        if (head.ValueKind != JsonValueKind.Object
            || !head.TryGetProperty("commit", out var commit) || commit.ValueKind != JsonValueKind.Object
            || !commit.TryGetProperty("statusCheckRollup", out var rollup) || rollup.ValueKind != JsonValueKind.Object)
        {
            return GitHubCheckState.None;
        }

        return String(rollup, "state") switch
        {
            "SUCCESS" => GitHubCheckState.Passing,
            "FAILURE" or "ERROR" => GitHubCheckState.Failing,
            "PENDING" or "EXPECTED" => GitHubCheckState.Pending,
            _ => GitHubCheckState.None
        };
    }

    /// <summary>Merge commit, else squash, else rebase — the order the merge button
    /// lists them. A repository always allows at least one; were it to answer with
    /// none, merge commit is sent and GitHub's own refusal says why.</summary>
    private static GitHubMergeMethod PreferredMergeMethod(JsonElement repository)
    {
        static bool Allowed(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

        if (Allowed(repository, "mergeCommitAllowed")) return GitHubMergeMethod.Merge;
        if (Allowed(repository, "squashMergeAllowed")) return GitHubMergeMethod.Squash;
        if (Allowed(repository, "rebaseMergeAllowed")) return GitHubMergeMethod.Rebase;

        return GitHubMergeMethod.Merge;
    }

    public async Task<GitHubUploadedFile> UploadFileAsync(
        GitHubRepositoryRef repository,
        string path,
        string branch,
        byte[] content,
        string commitMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(content);

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new GitHubException("A committed file needs a path.");
        }

        if (string.IsNullOrWhiteSpace(branch))
        {
            throw new GitHubException("A committed file needs a branch.");
        }

        await EnsureBranchExistsAsync(repository, branch, cancellationToken).ConfigureAwait(false);

        var payload = new Dictionary<string, object?>
        {
            ["message"] = commitMessage,
            ["content"] = Convert.ToBase64String(content),
            ["branch"] = branch
        };

        var response = await transport.SendAsync(
            HttpMethod.Put,
            $"repos/{repository.Owner}/{repository.Name}/contents/{Uri.EscapeDataString(path)}",
            payload,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var downloadUrl = response.TryGetProperty("content", out var contentElement)
            && contentElement.ValueKind == JsonValueKind.Object
            ? String(contentElement, "download_url")
            : null;

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new GitHubException("GitHub did not return a download URL for the committed file.");
        }

        return new GitHubUploadedFile(path, downloadUrl);
    }

    public async Task<GitHubCommittedFile> CommitFileAsync(
        GitHubRepositoryRef repository,
        string path,
        byte[] content,
        string commitMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(content);

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new GitHubException("A committed file needs a path.");
        }

        // Segment by segment, so a path with folders in it stays a path with
        // folders in it. Escaping the whole thing would send the slashes as
        // %2F, which names one file with a slash in its name.
        var resource = $"repos/{repository.Owner}/{repository.Name}/contents/"
            + string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

        string? existingSha = null;
        try
        {
            var existing = await transport.SendAsync(HttpMethod.Get, resource, body: null, cancellationToken: cancellationToken).ConfigureAwait(false);
            existingSha = existing.ValueKind == JsonValueKind.Object ? String(existing, "sha") : null;
        }
        catch (GitHubException ex) when (ex.IsNotFound)
        {
            // Not committed yet. A missing repository answers the same status,
            // and is told apart by the PUT below failing with GitHub's words
            // for it rather than by guessing here.
        }

        var blobSha = GitBlobSha(content);
        if (string.Equals(existingSha, blobSha, StringComparison.OrdinalIgnoreCase))
        {
            return new GitHubCommittedFile(path, blobSha, Committed: false);
        }

        var payload = new Dictionary<string, object?>
        {
            ["message"] = commitMessage,
            ["content"] = Convert.ToBase64String(content)
        };
        if (existingSha is not null) payload["sha"] = existingSha;

        JsonElement response;
        try
        {
            response = await transport.SendAsync(HttpMethod.Put, resource, payload, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (GitHubException ex) when (ex.IsNotFound)
        {
            // The file not being there was handled above, so a not-found on the
            // write is the repository: the token transport already says so in
            // these words, and the CLI one says "gh: Not Found (HTTP 404)".
            throw new GitHubException(
                "GitHub couldn't find that repository — check the owner/repo and that the signed-in account can write to it.",
                ex)
            {
                Status = ex.Status
            };
        }

        var committedSha = response.TryGetProperty("content", out var contentElement)
            && contentElement.ValueKind == JsonValueKind.Object
            ? String(contentElement, "sha")
            : null;

        return new GitHubCommittedFile(path, committedSha ?? blobSha, Committed: true);
    }

    /// <summary>The id git gives a blob with these bytes — SHA-1 over
    /// <c>blob {length}\0</c> and the bytes — which is what the Contents API
    /// reports as a file's <c>sha</c>. Computing it here is what lets an
    /// unchanged file be recognised from one GET rather than a download.</summary>
    internal static string GitBlobSha(byte[] content)
    {
        var header = System.Text.Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        using var sha1 = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA1);
        sha1.AppendData(header);
        sha1.AppendData(content);
        return Convert.ToHexStringLower(sha1.GetHashAndReset());
    }

    /// <summary>Creates <paramref name="branch"/> off the repository's default
    /// branch when it does not already exist. The Contents API commits onto an
    /// existing branch only — it will not create one implicitly.</summary>
    private async Task EnsureBranchExistsAsync(GitHubRepositoryRef repository, string branch, CancellationToken cancellationToken)
    {
        try
        {
            await transport.SendAsync(
                HttpMethod.Get,
                $"repos/{repository.Owner}/{repository.Name}/git/ref/heads/{Uri.EscapeDataString(branch)}",
                body: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (GitHubException)
        {
            // Doesn't exist yet (or the lookup failed for some other reason, in
            // which case the branch/commit calls below fail with a clearer
            // message than a bare 404 on the ref lookup would have).
        }

        var repositoryDetails = await transport.SendAsync(
            HttpMethod.Get,
            $"repos/{repository.Owner}/{repository.Name}",
            body: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var defaultBranch = String(repositoryDetails, "default_branch");
        if (string.IsNullOrWhiteSpace(defaultBranch))
        {
            throw new GitHubException("GitHub did not report a default branch to branch from.");
        }

        var defaultRef = await transport.SendAsync(
            HttpMethod.Get,
            $"repos/{repository.Owner}/{repository.Name}/git/ref/heads/{Uri.EscapeDataString(defaultBranch)}",
            body: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var sha = defaultRef.TryGetProperty("object", out var target) && target.ValueKind == JsonValueKind.Object
            ? String(target, "sha")
            : null;

        if (string.IsNullOrWhiteSpace(sha))
        {
            throw new GitHubException("GitHub did not return a commit to branch from.");
        }

        await transport.SendAsync(
            HttpMethod.Post,
            $"repos/{repository.Owner}/{repository.Name}/git/refs",
            new Dictionary<string, object?> { ["ref"] = $"refs/heads/{branch}", ["sha"] = sha },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the issue payload GitHub returns for both create and get.</summary>
    internal static GitHubIssue ReadIssue(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new GitHubException("GitHub did not return an issue.");
        }

        var number = element.TryGetProperty("number", out var n) && n.TryGetInt32(out var value) ? value : 0;
        if (number == 0) throw new GitHubException("GitHub returned an issue without a number.");

        return new GitHubIssue(
            number,
            String(element, "html_url") ?? string.Empty,
            String(element, "title") ?? string.Empty,
            string.Equals(String(element, "state"), "closed", StringComparison.OrdinalIgnoreCase)
                ? GitHubItemState.Closed
                : GitHubItemState.Open,
            Timestamp(element, "updated_at"));
    }

    /// <summary>A pull request as the pulls endpoint returns it. Merged is read from
    /// <c>merged_at</c>: <c>state</c> says "closed" for merged and abandoned
    /// alike.</summary>
    internal static GitHubPullRequest ReadPullRequest(JsonElement element, string repositoryFullName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new GitHubException("GitHub did not return a pull request.");
        }

        var number = element.TryGetProperty("number", out var n) && n.TryGetInt32(out var value) ? value : 0;
        if (number == 0) throw new GitHubException("GitHub returned a pull request without a number.");

        var state = Timestamp(element, "merged_at") is not null
            ? GitHubItemState.Merged
            : string.Equals(String(element, "state"), "closed", StringComparison.OrdinalIgnoreCase)
                ? GitHubItemState.Closed
                : element.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True
                    ? GitHubItemState.Draft
                    : GitHubItemState.Open;

        return new GitHubPullRequest(
            number,
            String(element, "html_url") ?? $"https://github.com/{repositoryFullName}/pull/{number}",
            String(element, "title") ?? string.Empty,
            state,
            repositoryFullName);
    }

    /// <summary>
    /// Picks the pull requests out of an issue timeline. A pull request that says
    /// "closes #12" shows up as a <c>cross-referenced</c> event whose source is
    /// an issue carrying a <c>pull_request</c> object — that object is also where
    /// merged-ness lives, which the plain state field cannot tell you.
    /// </summary>
    internal static IReadOnlyList<GitHubPullRequest> ReadLinkedPullRequests(JsonElement timeline)
    {
        if (timeline.ValueKind != JsonValueKind.Array) return [];

        var found = new Dictionary<string, GitHubPullRequest>(StringComparer.Ordinal);

        foreach (var item in timeline.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.Object) continue;
            if (!source.TryGetProperty("issue", out var issue) || issue.ValueKind != JsonValueKind.Object) continue;
            if (!issue.TryGetProperty("pull_request", out var pull) || pull.ValueKind != JsonValueKind.Object) continue;

            var number = issue.TryGetProperty("number", out var n) && n.TryGetInt32(out var value) ? value : 0;
            if (number == 0) continue;

            var url = String(issue, "html_url") ?? String(pull, "html_url") ?? string.Empty;

            var state = Timestamp(pull, "merged_at") is not null
                ? GitHubItemState.Merged
                : string.Equals(String(issue, "state"), "closed", StringComparison.OrdinalIgnoreCase)
                    ? GitHubItemState.Closed
                    : issue.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True
                        ? GitHubItemState.Draft
                        : GitHubItemState.Open;

            string? repositoryFullName = null;
            if (issue.TryGetProperty("repository", out var repo) && repo.ValueKind == JsonValueKind.Object)
            {
                repositoryFullName = String(repo, "full_name");
            }

            // The same pull request can be cross-referenced more than once; the
            // later event carries the fresher state.
            found[$"{repositoryFullName}#{number}"] = new GitHubPullRequest(
                number,
                url,
                String(issue, "title") ?? string.Empty,
                state,
                repositoryFullName);
        }

        return [.. found.Values.OrderBy(p => p.Number)];
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
