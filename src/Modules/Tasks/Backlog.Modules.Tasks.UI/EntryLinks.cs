using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>A pull request an entry's work opened, recorded by <c>link_change</c>.</summary>
public sealed record EntryPullRequestLink(string Repository, int Number)
{
    public string Url => $"https://github.com/{Repository}/pull/{Number}";

    public string Label => $"PR #{Number}";
}

/// <summary>An AI session that worked on an entry, recorded by <c>link_session</c>.</summary>
public sealed record EntrySessionLink(string Repository, string SessionId)
{
    /// <summary>The id's first eight characters — a Claude session GUID's first
    /// block, which is how one is told apart at a glance — whatever the id's shape;
    /// the whole id is in the tooltip.</summary>
    public string ShortId => SessionId.Length <= 8 ? SessionId : SessionId[..8];
}

/// <summary>
/// The pull requests and sessions an entry's projections name, in the order they
/// were recorded, each once.
/// <para>
/// Beside <see cref="TasksIssues.FindLink"/> rather than inside it: an issue is
/// what the entry was filed as and there is at most one, while these are what
/// work on the entry produced and there may be several. A projection whose
/// repository is not <c>owner/name</c>, or whose pull request number is not a
/// number, is left out rather than drawn as a link to nowhere.
/// </para>
/// </summary>
public static class EntryLinks
{
    public static IReadOnlyList<EntryPullRequestLink> PullRequests(TaskItemDto entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return [.. entry.Projections
            .Where(p => Is(p, EntryProjectionDto.PullRequestTargetType) && IsRepository(p.RepoId))
            .Select(p => int.TryParse(p.ExternalId, out var number) && number > 0
                ? new EntryPullRequestLink(p.RepoId, number)
                : null)
            .OfType<EntryPullRequestLink>()
            .Distinct()];
    }

    public static IReadOnlyList<EntrySessionLink> Sessions(TaskItemDto entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return [.. entry.Projections
            .Where(p => Is(p, EntryProjectionDto.SessionTargetType) && !string.IsNullOrWhiteSpace(p.ExternalId))
            .Select(p => new EntrySessionLink(p.RepoId, p.ExternalId.Trim()))
            .DistinctBy(link => link.SessionId, StringComparer.OrdinalIgnoreCase)];
    }

    private static bool Is(EntryProjectionDto projection, string targetType) =>
        string.Equals(projection.TargetType, targetType, StringComparison.OrdinalIgnoreCase);

    private static bool IsRepository(string repoId) => repoId.Split('/').Length == 2;
}
