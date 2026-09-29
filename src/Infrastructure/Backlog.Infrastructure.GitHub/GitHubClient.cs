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
            tolerate: OnlyTheCheckRollupWasRefused);

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

    /// <summary>One of the three merge mutations. Nothing in their payloads is read:
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
            throw new GitHubException("A pull request has to be read before it can be merged.");
        }

        var variables = new Dictionary<string, object?> { ["id"] = pullRequestId };
        if (method is { } chosen) variables["method"] = MergeMethodName(chosen);

        await GitHubGraphQl.SendAsync(transport, repository, mutation, variables, cancellationToken);
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
