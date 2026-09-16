using System.Text.Json;

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
            cancellationToken: cancellationToken);

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
            cancellationToken: cancellationToken);

        var issue = ReadIssue(issueResponse);

        IReadOnlyList<GitHubPullRequest> pullRequests;
        try
        {
            var timeline = await transport.SendAsync(
                HttpMethod.Get,
                $"repos/{repository.Owner}/{repository.Name}/issues/{number}/timeline?per_page=100",
                body: null,
                cancellationToken: cancellationToken);

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

        await EnsureBranchExistsAsync(repository, branch, cancellationToken);

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
            cancellationToken: cancellationToken);

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
            var existing = await transport.SendAsync(HttpMethod.Get, resource, body: null, cancellationToken: cancellationToken);
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
            response = await transport.SendAsync(HttpMethod.Put, resource, payload, cancellationToken: cancellationToken);
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
                cancellationToken: cancellationToken);
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
            cancellationToken: cancellationToken);

        var defaultBranch = String(repositoryDetails, "default_branch");
        if (string.IsNullOrWhiteSpace(defaultBranch))
        {
            throw new GitHubException("GitHub did not report a default branch to branch from.");
        }

        var defaultRef = await transport.SendAsync(
            HttpMethod.Get,
            $"repos/{repository.Owner}/{repository.Name}/git/ref/heads/{Uri.EscapeDataString(defaultBranch)}",
            body: null,
            cancellationToken: cancellationToken);

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
            cancellationToken: cancellationToken);
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

    private static DateTimeOffset? Timestamp(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
}
