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

        var references = repositories
            .Select(repository => settings.Current.Find(repository.Alias))
            .OfType<GitHubRepositoryRef>()
            .ToList();

        if (references.Count == 0) return ActivityBaseline.Empty;

        GitHubActivityBaseline answer;
        try
        {
            answer = await baseline
                .GetBaselineAsync(
                    references,
                    [.. blocks.Select(block => new ActivityBlock(block.From, block.To))],
                    login,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitHubException)
        {
            return ActivityBaseline.Empty with { Complete = false };
        }
        catch (GitHubNotConfiguredException)
        {
            return ActivityBaseline.Empty with { Complete = false };
        }

        return new ActivityBaseline(
            [.. answer.Blocks.Select(block => new ActivityVolume(
                block.From,
                block.To,
                block.MergedPullRequests,
                block.ClosedIssues))],
            answer.Complete);
    }
}
