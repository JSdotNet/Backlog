using System.Runtime.ExceptionServices;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Infrastructure.GitHub.Dashboard;

/// <summary>
/// Answers <see cref="IDriftIssueSource"/> from GitHub: the open issues the devbook
/// sweeps filed under <c>devbook-drift</c>, and which of them carry <c>sync-failed</c>.
/// </summary>
/// <remarks>
/// <para>
/// One labelled search per repository rather than every open issue filtered here: the
/// sweeps add <c>sync-failed</c> to the drift issue itself, so the one label finds both,
/// and a repository with a thousand open issues answers with the few that matter.
/// </para>
/// <para>
/// A repository that fails is skipped and the read marked incomplete, as
/// <see cref="GitHubActivitySource"/> does: the repositories that answered are true, and
/// the part says the list is a floor. When every repository asked fails, nothing was read,
/// and the first failure is thrown so the part says why — a rate limit, a repository
/// GitHub does not know — rather than listing nothing as though nothing were open.
/// </para>
/// </remarks>
internal sealed class GitHubDriftIssueSource(IGitHubClient client, GitHubSettingsStore settings) : IDriftIssueSource
{
    internal const string DriftLabel = "devbook-drift";

    internal const string SyncFailedLabel = "sync-failed";

    public Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(settings.Current.Repositories.Count == 0
            ? InsightAvailability.Unavailable("no repositories are configured. Add one in Settings and its drift issues appear here.")
            : InsightAvailability.Available);

    public async Task<DriftIssueRead> GetOpenAsync(
        IReadOnlyList<DashboardRepository> repositories,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repositories);

        var issues = new List<DriftIssue>();
        var complete = true;
        var read = 0;
        Exception? failure = null;

        foreach (var repository in repositories)
        {
            if (settings.Current.Find(repository.Alias) is not { } reference) continue;

            GitHubIssueSearchRead search;
            try
            {
                search = await client
                    .SearchOpenIssuesWithLabelAsync(reference, DriftLabel, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is GitHubException or GitHubNotConfiguredException)
            {
                complete = false;
                failure ??= exception;
                continue;
            }

            read++;
            complete &= !search.Truncated;

            issues.AddRange(search.Issues
                .Where(issue => issue.IsOpen)
                .Select(issue => new DriftIssue(
                    repository.Alias,
                    issue.Number,
                    issue.Title,
                    issue.Url,
                    issue.Labels.Contains(SyncFailedLabel, StringComparer.OrdinalIgnoreCase))));
        }

        if (read == 0 && failure is not null) ExceptionDispatchInfo.Throw(failure);

        return new DriftIssueRead(issues, complete, failure?.Message);
    }
}
