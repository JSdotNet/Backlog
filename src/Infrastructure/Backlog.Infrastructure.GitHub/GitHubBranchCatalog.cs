using System.Text.Json;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// Which branches a repository has, and where one of them currently points.
/// <para>
/// Separate from <see cref="IGitHubClient"/> rather than folded into it, because
/// that interface is deliberately narrow — "the operations the app actually
/// performs against GitHub", meaning the issue flow. Reading a repository's
/// branch list is not part of putting an entry on the board; it is what the
/// knowledge source needs to know which snapshot to fetch, and it has a
/// different lifetime and a different caller.
/// </para>
/// </summary>
public interface IGitHubBranchCatalog
{
    /// <summary>
    /// The repository's branches, in the order GitHub lists them.
    /// <para>
    /// One page, not every page. A branch selector is a list somebody picks from
    /// and a repository with more branches than one page holds is a repository
    /// where scrolling to the four-hundredth was never the interaction — the
    /// branch can still be typed. Paging the whole set would turn opening
    /// Settings into an unbounded number of calls.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<string>> ListBranchesAsync(
        GitHubRepositoryRef repository,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Where a branch currently points, resolving a null or blank
    /// <paramref name="branch"/> to the repository's own default branch.
    /// <para>
    /// The commit id is the whole point: it is what makes "is the snapshot I
    /// hold still the latest?" answerable in one cheap call, without downloading
    /// anything. Returns null when the branch does not exist, which is an
    /// ordinary thing for a stored setting to say after somebody deletes a
    /// branch on GitHub.
    /// </para>
    /// </summary>
    Task<GitHubBranchHead?> ResolveHeadAsync(
        GitHubRepositoryRef repository,
        string? branch,
        CancellationToken cancellationToken = default);
}

/// <summary>A branch and the commit it points at.</summary>
/// <param name="Branch">The resolved branch name — never null or blank, so a
/// caller that passed null gets back the name of the default branch rather than
/// having to ask again.</param>
/// <param name="Sha">The commit id at the tip.</param>
public sealed record GitHubBranchHead(string Branch, string Sha);

/// <inheritdoc />
public sealed class GitHubBranchCatalog(IGitHubTransport transport) : IGitHubBranchCatalog
{
    private readonly IGitHubTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public async Task<IReadOnlyList<string>> ListBranchesAsync(
        GitHubRepositoryRef repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var payload = await _transport.SendAsync(
            HttpMethod.Get,
            $"repos/{repository.Owner}/{repository.Name}/branches?per_page=100",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (payload.ValueKind is not JsonValueKind.Array) return [];

        return
        [
            .. payload.EnumerateArray()
                .Select(branch => branch.TryGetProperty("name", out var name) ? name.GetString() : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.Trim())
        ];
    }

    public async Task<GitHubBranchHead?> ResolveHeadAsync(
        GitHubRepositoryRef repository,
        string? branch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var name = string.IsNullOrWhiteSpace(branch)
            ? await DefaultBranchAsync(repository, cancellationToken).ConfigureAwait(false)
            : branch.Trim();

        if (string.IsNullOrWhiteSpace(name)) return null;

        // The branches endpoint rather than git/ref/heads, because a branch name
        // containing a slash — release/1.2, a perfectly ordinary name — is
        // ambiguous against the ref endpoint's path grammar and unambiguous here.
        JsonElement payload;
        try
        {
            payload = await _transport.SendAsync(
                HttpMethod.Get,
                $"repos/{repository.Owner}/{repository.Name}/branches/{Uri.EscapeDataString(name)}",
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (GitHubException)
        {
            // A branch that is not there is a stored setting that has gone stale,
            // not a fault to propagate: the caller shows "that branch is gone"
            // and offers the list, which is a better answer than an error dialog.
            return null;
        }

        var sha = payload.TryGetProperty("commit", out var commit) && commit.TryGetProperty("sha", out var value)
            ? value.GetString()
            : null;

        return string.IsNullOrWhiteSpace(sha) ? null : new GitHubBranchHead(name, sha.Trim());
    }

    private async Task<string?> DefaultBranchAsync(GitHubRepositoryRef repository, CancellationToken cancellationToken)
    {
        var details = await _transport.SendAsync(
            HttpMethod.Get,
            $"repos/{repository.Owner}/{repository.Name}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return details.TryGetProperty("default_branch", out var value) ? value.GetString() : null;
    }
}
