namespace Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

/// <summary>
/// One pull request merged within the recently-merged window, as the pull requests
/// list's Recently merged view shows it: where it lives, whose it is, which branch went
/// where, and when and by whom it was merged.
/// <para>
/// Its own record rather than a <see cref="PullRequestStatus"/> with a merge time,
/// because nearly everything that one carries — checks, merge state, the handle — is
/// what an act on an open pull request needs, and a merged one has no act left.
/// </para>
/// </summary>
/// <param name="MergedByLogin">Who merged it; null where GitHub no longer knows the
/// account.</param>
public sealed record MergedPullRequest(
    string RepositoryFullName,
    int Number,
    string Url,
    string Title,
    string HeadRefName,
    string BaseRefName,
    string? AuthorLogin,
    bool ViewerDidAuthor,
    DateTimeOffset MergedAt,
    string? MergedByLogin)
{
    /// <summary>The label names, as GitHub spells them.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];

    /// <summary>The issues this pull request closed.</summary>
    public IReadOnlyList<IssueReference> ClosingIssues { get; init; } = [];
}
