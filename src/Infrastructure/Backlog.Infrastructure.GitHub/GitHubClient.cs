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
