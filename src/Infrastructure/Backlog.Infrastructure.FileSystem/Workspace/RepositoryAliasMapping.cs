using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// A relabel's alias mapping as the local stores key it (local ADR 0060 §3): both sides
/// normalized, a blank side or an alias mapped onto itself left out, because neither is
/// a move. A type of its own so a lookup always normalizes the alias it is asked about.
/// </summary>
internal sealed class RepositoryAliasMapping
{
    private readonly Dictionary<string, string> _oldToNew;

    private RepositoryAliasMapping(Dictionary<string, string> oldToNew) => _oldToNew = oldToNew;

    public static RepositoryAliasMapping Of(IReadOnlyDictionary<string, string> oldToNew)
    {
        ArgumentNullException.ThrowIfNull(oldToNew);

        var mapping = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (from, to) in oldToNew)
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) continue;

            var old = GitHubRepositoryRef.NormalizeAlias(from);
            var target = GitHubRepositoryRef.NormalizeAlias(to);
            if (!string.Equals(old, target, StringComparison.Ordinal)) mapping[old] = target;
        }

        return new RepositoryAliasMapping(mapping);
    }

    /// <summary>The alias <paramref name="alias"/> moves to, or null when the mapping
    /// does not name it.</summary>
    public string? MovedTo(string? alias) =>
        !string.IsNullOrWhiteSpace(alias) && _oldToNew.TryGetValue(GitHubRepositoryRef.NormalizeAlias(alias), out var target)
            ? target
            : null;

    /// <summary>Whether the mapping moves <paramref name="alias"/>.</summary>
    public bool Moves(string? alias) => MovedTo(alias) is not null;
}
