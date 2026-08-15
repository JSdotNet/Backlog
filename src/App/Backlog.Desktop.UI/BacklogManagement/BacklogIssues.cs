using Backlog.Desktop.UI.Workspace;
using Backlog.Modules.Backlog.Abstractions;
using Backlog.Modules.Backlog.Abstractions.DataTransferObjects;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.BacklogManagement;

/// <summary>
/// What a backlog entry looks like once it becomes a GitHub issue.
/// <para>
/// The connection to GitHub is a workspace concern shared with everything else
/// that files issues; only the shape of the issue is Backlog Management's — the
/// title is the entry's, the body is its markdown, the labels are its tags, and
/// a sub-item says which entry it came from. That is the whole of what lives
/// here, which is why it is a few lines over
/// <see cref="GitHubIntegration"/> rather than a service of its own.
/// </para>
/// </summary>
public sealed class BacklogIssues(GitHubIntegration gitHub)
{
    /// <summary>The issue an entry was already pushed to, or null.</summary>
    public static GitHubIssueLink? FindLink(BacklogEntryDto entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var projection = entry.Projections.LastOrDefault(
            p => string.Equals(p.TargetType, GitHubIntegration.IssueTargetType, StringComparison.OrdinalIgnoreCase));

        if (projection is null) return null;
        if (!int.TryParse(projection.ExternalId, out var number)) return null;

        var parts = projection.RepoId.Split('/', 2);
        if (parts.Length != 2) return null;

        return new GitHubIssueLink(projection.RepoId, number);
    }

    /// <summary>Files the issue for an entry and hands back the link. Recording
    /// it on the entry is the module's job — nothing here can change one.</summary>
    public Task<GitHubIssueLink> PushAsync(
        BacklogEntryDto entry,
        GitHubRepositoryRef repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (FindLink(entry) is { } existing)
        {
            throw new GitHubException($"This entry is already {existing.RepoFullName}#{existing.IssueNumber}.");
        }

        return gitHub.CreateIssueAsync(repository, entry.Title, entry.Body, entry.Tags, cancellationToken);
    }

    public Task<GitHubIssueLink> PushSubItemAsync(
        string parentTitle,
        EntryTextParser.ParsedSubItem subItem,
        GitHubRepositoryRef repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subItem);

        return gitHub.CreateIssueAsync(
            repository,
            subItem.Title,
            BuildSubItemBody(parentTitle, subItem),
            subItem.MetadataTags,
            cancellationToken);
    }

    private static string BuildSubItemBody(string parentTitle, EntryTextParser.ParsedSubItem subItem) =>
        $"""
        From backlog entry: {parentTitle}

        {subItem.Notes}
        """.Trim();
}
