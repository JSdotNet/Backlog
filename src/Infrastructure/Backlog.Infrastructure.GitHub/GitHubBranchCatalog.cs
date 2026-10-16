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
    /// Every page, up to <see cref="GitHubBranchCatalog.BranchListLimit"/>
    /// branches. The list is what the source picker searches, so a branch left
    /// off it could neither be found nor typed — one page of a hundred hid
    /// seventy-four of this repository's own. The ceiling keeps a repository
    /// with thousands of branches from turning one click into dozens of calls;
    /// a list that reaches it may be missing some, and the picker asks GitHub
    /// for a typed name it does not hold before saying there is no such branch.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<string>> ListBranchesAsync(
        GitHubRepositoryRef repository,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The repository's branches with the commit each points at, in the order
    /// GitHub lists them, paged to the same ceiling as
    /// <see cref="ListBranchesAsync"/>.
    /// <para>
    /// The same calls as the names alone: GitHub puts each branch's commit on its
    /// row of the listing. A row with no commit is left out, since it cannot say
    /// where the branch points.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<GitHubBranchHead>> ListBranchHeadsAsync(
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
    /// <summary>The most branches a listing reads: ten pages of a hundred.</summary>
    public const int BranchListLimit = 1000;

    /// <summary>The page size GitHub allows at most for branches.</summary>
    private const int PageSize = 100;

    private readonly IGitHubTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public async Task<IReadOnlyList<string>> ListBranchesAsync(
        GitHubRepositoryRef repository,
        CancellationToken cancellationToken = default)
    {
        var rows = await ListRowsAsync(repository, cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(row => row.Name)];
    }

    public async Task<IReadOnlyList<GitHubBranchHead>> ListBranchHeadsAsync(
        GitHubRepositoryRef repository,
        CancellationToken cancellationToken = default)
    {
        var rows = await ListRowsAsync(repository, cancellationToken).ConfigureAwait(false);
        return [.. rows.Where(row => row.Sha is not null).Select(row => new GitHubBranchHead(row.Name, row.Sha!))];
    }

    /// <summary>Every named row of the listing, up to the ceiling, with the commit
    /// the row carries where it carries one.</summary>
    private async Task<List<(string Name, string? Sha)>> ListRowsAsync(
        GitHubRepositoryRef repository,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var rows = new List<(string Name, string? Sha)>();

        // Paged until a short page rather than by the Link header: the transport
        // hands back the parsed body only, and a page holding fewer than asked
        // for is GitHub's own way of saying it was the last.
        for (var page = 1; rows.Count < BranchListLimit; page++)
        {
            var payload = await _transport.SendAsync(
                HttpMethod.Get,
                $"repos/{repository.Owner}/{repository.Name}/branches?per_page={PageSize}&page={page}",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (payload.ValueKind is not JsonValueKind.Array) break;

            var count = payload.GetArrayLength();

            foreach (var branch in payload.EnumerateArray())
            {
                if (branch.ValueKind is not JsonValueKind.Object
                    || !branch.TryGetProperty("name", out var name)
                    || name.ValueKind is not JsonValueKind.String
                    || string.IsNullOrWhiteSpace(name.GetString()))
                {
                    continue;
                }

                var sha = branch.TryGetProperty("commit", out var commit)
                          && commit.ValueKind is JsonValueKind.Object
                          && commit.TryGetProperty("sha", out var value)
                          && value.ValueKind is JsonValueKind.String
                    ? value.GetString()
                    : null;

                rows.Add((name.GetString()!.Trim(), string.IsNullOrWhiteSpace(sha) ? null : sha.Trim()));
            }

            if (count < PageSize) break;
        }

        return rows.Count > BranchListLimit ? rows.GetRange(0, BranchListLimit) : rows;
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
