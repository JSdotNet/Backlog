using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>A pull request an entry's work opened, recorded by <c>link_change</c>.</summary>
public sealed record EntryPullRequestLink(string Repository, int Number)
{
    public string Url => $"https://github.com/{Repository}/pull/{Number}";

    /// <summary>The number alone: the GitHub mark beside it says where it lives, and
    /// the tooltip says it is a pull request.</summary>
    public string Label => $"#{Number}";
}

/// <summary>A pull request projection as the entry stores it, unparsed: <c>link_change</c>
/// keeps a number as given, so <paramref name="ExternalId"/> may read <c>#582</c> and
/// <paramref name="Repository"/> may not be <c>owner/name</c>. What another module is told an
/// entry links, where <see cref="EntryPullRequestLink"/> is what can be drawn as a link.</summary>
public sealed record EntryPullRequestProjection(string Repository, string ExternalId);

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

    /// <summary>The pull request projections as stored, each once, unparsed and untrimmed.
    /// Read the way <c>ProgressTaskSource</c> publishes them to Progress, the exact target type
    /// included, so the Goals view's rows read what the goal's bar reads.</summary>
    public static IReadOnlyList<EntryPullRequestProjection> PullRequestProjections(TaskItemDto entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return [.. entry.Projections
            .Where(p => p.TargetType == EntryProjectionDto.PullRequestTargetType)
            .Select(p => new EntryPullRequestProjection(p.RepoId, p.ExternalId))
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
