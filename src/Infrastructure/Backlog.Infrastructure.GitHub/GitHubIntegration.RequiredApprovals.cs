using System.Collections.Concurrent;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// The approvals a pull request's base branch requires, on the rows the open list and
/// the pins read (<c>.devbook/domain/progress/features.md#pull-request-status</c>).
/// <para>
/// Read once per base branch rather than per row, through
/// <see cref="IGitHubClient.ReadRequiredApprovalsAsync"/>, after the list read and
/// beside it: the list query stays as it is, and so does the slim linked read, which a
/// task's links read in bulk. A repository's open pull requests target a handful of
/// branches, and a branch's rules change seldom, so what was read is kept for
/// <see cref="RequiredApprovalsFreshFor"/> and the next refresh of the list costs no
/// second round trip.
/// </para>
/// <para>
/// Something extra on rows the list already has: a branch whose rules could not be read
/// leaves its rows' <see cref="GitHubReviewSummary.RequiredRead"/> false — an approvals
/// count with no verdict — and never fails the list.
/// </para>
/// </summary>
public sealed partial class GitHubIntegration
{
    /// <summary>How long a branch's required approvals are kept before they are read
    /// again. Long against the list's minute: an administrator changing a branch's rules
    /// is rare, and a row that says "1/2" for ten minutes after the rule moved to one is
    /// a smaller cost than a round trip on every refresh. A branch whose rules could not
    /// be read is kept as unread for as long, so a token refused them is not asked again
    /// on every refresh either.</summary>
    public static readonly TimeSpan RequiredApprovalsFreshFor = TimeSpan.FromMinutes(10);

    /// <summary>What one branch's rules said: read or not, the required total where read,
    /// and when.</summary>
    private readonly record struct BranchRules(bool Read, int? Required, DateTimeOffset ReadAt);

    private readonly ConcurrentDictionary<string, BranchRules> _requiredApprovals = new(StringComparer.Ordinal);

    /// <summary>
    /// <paramref name="pulls"/>, each open one carrying the approvals its base branch
    /// requires where they could be read. Only an answer younger than
    /// <see cref="RequiredApprovalsFreshFor"/> is drawn, so a branch whose re-read fails
    /// goes back to no verdict rather than keeping an old one. Merged and closed rows are
    /// left as they are: no status line draws their approvals.
    /// </summary>
    private async Task<IReadOnlyList<GitHubOpenPullRequest>> WithRequiredApprovalsAsync(
        GitHubRepositoryRef repository,
        IReadOnlyList<GitHubOpenPullRequest> pulls,
        CancellationToken cancellationToken)
    {
        string[] branches =
        [
            .. pulls
                .Where(pull => pull.IsOpen && !string.IsNullOrWhiteSpace(pull.BaseRefName))
                .Select(pull => pull.BaseRefName)
                .Distinct(StringComparer.Ordinal)
        ];
        if (branches.Length == 0) return pulls;

        var now = (time ?? TimeProvider.System).GetUtcNow();
        string[] stale = [.. branches.Where(branch => Fresh(repository, branch, now) is null)];

        if (stale.Length > 0)
        {
            IReadOnlyDictionary<string, int?> read;
            try
            {
                read = await client.ReadRequiredApprovalsAsync(repository, stale, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is GitHubException or GitHubNotConfiguredException or HttpRequestException
                || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                read = new Dictionary<string, int?>();
            }

            PruneExpired(now);
            foreach (var branch in stale)
            {
                _requiredApprovals[BranchKey(repository, branch)] = read.TryGetValue(branch, out var required)
                    ? new BranchRules(Read: true, required, now)
                    : new BranchRules(Read: false, null, now);
            }
        }

        return
        [
            .. pulls.Select(pull => pull.IsOpen && Fresh(repository, pull.BaseRefName, now) is { Read: true } known
                ? pull with { Reviews = pull.Reviews with { Required = known.Required, RequiredRead = true } }
                : pull)
        ];
    }

    /// <summary>The branch's answer while it is younger than the window, else null.</summary>
    private BranchRules? Fresh(GitHubRepositoryRef repository, string branch, DateTimeOffset now) =>
        _requiredApprovals.TryGetValue(BranchKey(repository, branch), out var known) && now - known.ReadAt < RequiredApprovalsFreshFor
            ? known
            : null;

    /// <summary>Drops every answer past the window, so branches long merged away do not
    /// accumulate for the life of the adapter.</summary>
    private void PruneExpired(DateTimeOffset now)
    {
        foreach (var (key, known) in _requiredApprovals)
        {
            if (now - known.ReadAt >= RequiredApprovalsFreshFor) _requiredApprovals.TryRemove(key, out _);
        }
    }

    /// <summary>A branch's key: the repository without regard to case, the way GitHub
    /// compares it, and the branch exactly as spelled, the way Git compares it.</summary>
    private static string BranchKey(GitHubRepositoryRef repository, string branch) =>
        $"{repository.FullName.ToLowerInvariant()}:{branch}";
}
