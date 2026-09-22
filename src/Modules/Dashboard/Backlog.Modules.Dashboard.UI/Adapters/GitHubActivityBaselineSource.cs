using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.UI.Adapters;

/// <summary>
/// Answers <see cref="IActivityBaselineSource"/> from GitHub: how much the signed-in
/// person merged and closed in each of a series of past blocks.
/// </summary>
/// <remarks>
/// <para>
/// Whose work is resolved here rather than passed in, exactly as
/// <see cref="GitHubActivitySource"/> resolves it. The dashboard is a personal view,
/// and a login travelling through the port would make the module able to report on
/// other people — which is a different product.
/// </para>
/// <para>
/// And resolved per repository, as that adapter does: the search queries carry
/// <c>author:</c>, and a repository bound to a second account holds work authored
/// by that account's login. So the repositories are grouped by the login they are
/// worked as, the client is asked once per group, and the counts are summed — the
/// record is still one person's, read under every name they work under.
/// </para>
/// <para>
/// A repository that Settings no longer holds is skipped rather than failing the
/// answer, on the same precedent: five repositories where one has been renamed
/// should set the bar from the four that are still true. What the reader must not
/// get is a silent zero where a refusal happened, and that is what the client's
/// <c>Complete</c> flag carries through untouched.
/// </para>
/// <para>
/// No availability method on this port and none needed. It rides behind
/// <see cref="IActivitySource"/>'s availability — the score has already established
/// that GitHub can be reached before it asks for a baseline — and a second
/// round trip to ask the same question would buy nothing.
/// </para>
/// </remarks>
internal sealed class GitHubActivityBaselineSource(
    IGitHubActivityBaselineClient baseline,
    IGitHubIdentityClient identity,
    GitHubSettingsStore settings) : IActivityBaselineSource
{
    public async Task<ActivityBaseline> GetBaselineAsync(
        IReadOnlyList<DashboardRepository> repositories,
        IReadOnlyList<ActivityWindow> blocks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        ArgumentNullException.ThrowIfNull(blocks);

        if (repositories.Count == 0 || blocks.Count == 0) return ActivityBaseline.Empty;

        var login = await identity.GetLoginAsync(cancellationToken).ConfigureAwait(false);

        // No author, no baseline — and an empty one rather than a zeroed one, so the
        // score drops its volume inputs instead of reading the reader as idle.
        if (string.IsNullOrWhiteSpace(login)) return ActivityBaseline.Empty;

        var current = settings.Current;

        var byAuthor = repositories
            .Select(repository => current.Find(repository.Alias))
            .OfType<GitHubRepositoryRef>()
            .GroupBy(
                reference => GitHubActivitySource.AuthorOf(current, reference, login),
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (byAuthor.Count == 0) return ActivityBaseline.Empty;

        IReadOnlyList<ActivityBlock> asked = [.. blocks.Select(block => new ActivityBlock(block.From, block.To))];

        var merged = new int[blocks.Count];
        var closed = new int[blocks.Count];
        var complete = true;

        // A group that refuses is left out and the answer marked incomplete, rather
        // than emptying the record the other groups did set: that is the rule the
        // client applies per query and the activity adapter per repository, and a
        // rate limit on one account's searches should not take the volume score off
        // the card for work read under another. What the reader must still not get
        // is a bar quietly set from half the record, and the flag is what says so.
        foreach (var group in byAuthor)
        {
            GitHubActivityBaseline answer;
            try
            {
                answer = await baseline
                    .GetBaselineAsync([.. group], asked, group.Key, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (GitHubException)
            {
                complete = false;
                continue;
            }
            catch (GitHubNotConfiguredException)
            {
                complete = false;
                continue;
            }

            complete &= answer.Complete;

            // The client answers one row per block it was asked for, in order; an
            // empty answer is the client's own "nothing to ask" and adds nothing.
            for (var index = 0; index < answer.Blocks.Count && index < blocks.Count; index++)
            {
                merged[index] += answer.Blocks[index].MergedPullRequests;
                closed[index] += answer.Blocks[index].ClosedIssues;
            }
        }

        return new ActivityBaseline(
            [.. blocks.Select((block, index) => new ActivityVolume(
                block.From,
                block.To,
                merged[index],
                closed[index]))],
            complete);
    }
}
