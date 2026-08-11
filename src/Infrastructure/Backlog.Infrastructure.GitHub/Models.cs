using System.Text.Json;

namespace Backlog.Infrastructure.GitHub;

internal static class GitHubJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>Where an issue or pull request currently stands. Merged is kept
/// distinct from closed because for a pull request they mean opposite
/// things.</summary>
public enum GitHubItemState
{
    Open,
    Draft,
    Merged,
    Closed
}

/// <summary>A GitHub issue as the app cares about it: enough to link to it and
/// to say whether it is still open.</summary>
public sealed record GitHubIssue(
    int Number,
    string Url,
    string Title,
    GitHubItemState State,
    DateTimeOffset? UpdatedAt);

/// <summary>A pull request that references an issue.</summary>
public sealed record GitHubPullRequest(
    int Number,
    string Url,
    string Title,
    GitHubItemState State,
    string? RepositoryFullName);

/// <summary>Everything one refresh learned about a pushed entry: the issue and
/// the pull requests that mention it.</summary>
public sealed record GitHubIssueSnapshot(
    GitHubIssue Issue,
    IReadOnlyList<GitHubPullRequest> PullRequests,
    DateTimeOffset RetrievedAt)
{
    /// <summary>The pull request worth showing when there is only room for one:
    /// a merged one if any, otherwise the most recently opened.</summary>
    public GitHubPullRequest? Headline =>
        PullRequests.FirstOrDefault(p => p.State == GitHubItemState.Merged)
        ?? PullRequests.FirstOrDefault(p => p.State is GitHubItemState.Open or GitHubItemState.Draft)
        ?? PullRequests.FirstOrDefault();
}
