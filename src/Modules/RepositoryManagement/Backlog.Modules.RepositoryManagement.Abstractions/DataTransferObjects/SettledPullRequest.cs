namespace Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

/// <summary>
/// A merged pull request as it is remembered across a restart: a settled fact
/// (glossary), which cannot change again (local ADR 0042 §3). Where it went and when,
/// what a reader draws it with, and what the pane finds its task and session by — the
/// head branch and the issues it closed.
/// <para>
/// Deliberately without checks, reviews, auto-merge, mergeability or how far it is
/// behind: those belong to an open pull request, and a stale one kept across a restart
/// would offer a merge GitHub no longer allows.
/// </para>
/// </summary>
/// <param name="BaseRefName">The branch it merged into, or null where the read did not
/// say.</param>
/// <param name="DefaultBranch">The repository's default branch at the read, or null
/// where the read did not say.</param>
public sealed record SettledPullRequest(
    string RepositoryFullName,
    int Number,
    DateTimeOffset? MergedAt,
    string? BaseRefName,
    string? DefaultBranch,
    string Title,
    string Url,
    string? AuthorLogin)
{
    /// <summary>The pull request by repository and number.</summary>
    public PullRequestReference Reference => new(RepositoryFullName, Number);

    /// <summary>The branch it merged from, or empty where the read did not say.</summary>
    public string HeadRefName { get; init; } = "";

    /// <summary>The issues it closed.</summary>
    public IReadOnlyList<IssueReference> ClosingIssues { get; init; } = [];

    /// <summary>The label names, as GitHub spells them.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];
}
