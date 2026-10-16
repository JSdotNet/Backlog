using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The three-dot comparisons GitHub drew this session, shared by the branch
/// overview and the branch review.
/// <para>
/// Keyed by repository and the two commits compared, never by branch name: a
/// branch that moves is a new pair of commits and is compared again, and the
/// overview's answer for a branch is the review's answer for it when the reader
/// opens it next. Bounded and least recently used out, since a comparison holds
/// up to three hundred files and as many commits.
/// </para>
/// </summary>
public sealed class DevbookBranchComparisons(IGitHubBranchComparison comparisons)
{
    private const int Capacity = 64;

    private readonly IGitHubBranchComparison _comparisons = comparisons ?? throw new ArgumentNullException(nameof(comparisons));
    private readonly BoundedTaskCache<GitHubComparison> _cache = new(Capacity);

    /// <summary>What <paramref name="headSha"/> has that <paramref name="baseSha"/>
    /// has not, read once per pair and shared by every caller asking at once.</summary>
    /// <param name="cancellationToken">Stops this caller's wait, never the read.</param>
    /// <param name="readDeadline">Stops the read itself, when this call is the
    /// one that starts it: a deadline of the caller's own work rather than of a
    /// person (the branch overview's load timeout), so a load that ran out of
    /// time stops spending the rate limit. A read cancelled this way is dropped
    /// from the cache, and the next ask reads again.</param>
    /// <exception cref="GitHubException">GitHub refused.</exception>
    public Task<GitHubComparison> CompareAsync(
        GitHubRepositoryRef repository,
        string baseSha,
        string headSha,
        CancellationToken cancellationToken = default,
        CancellationToken readDeadline = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return _cache.ReadAsync(
            $"{repository.FullName.ToLowerInvariant()}|{baseSha}|{headSha}",
            () => _comparisons.CompareAsync(repository, baseSha, headSha, readDeadline),
            cancellationToken);
    }
}
