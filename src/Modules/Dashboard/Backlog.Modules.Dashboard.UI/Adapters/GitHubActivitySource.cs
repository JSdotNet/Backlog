using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.UI.Adapters;

/// <summary>
/// Answers <see cref="IActivitySource"/> from GitHub: the person's own merged pull
/// requests and closed issues, with the churn detail behind them.
/// </summary>
/// <remarks>
/// <para>
/// Whose activity is resolved here rather than passed in. The dashboard is a
/// personal view, and a login travelling through the port would make the module
/// able to report on other people — which is a different product.
/// </para>
/// <para>
/// A repository that fails is skipped rather than failing the fetch. Five
/// repositories where one has been renamed should show four repositories' figures
/// and not an unavailable part, because the four are true.
/// </para>
/// </remarks>
internal sealed class GitHubActivitySource(
    IGitHubActivityClient activity,
    IGitHubIdentityClient identity,
    GitHubSettingsStore settings) : IActivitySource
{
    public async Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var available = await activity.GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);

        if (!available.IsAvailable) return InsightAvailability.Unavailable(available.Reason);

        var login = await identity.GetLoginAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(login))
        {
            return InsightAvailability.Unavailable(
                "GitHub did not say who you are signed in as, so there is no author to report on.");
        }

        if (settings.Current.Repositories.Count == 0)
        {
            return InsightAvailability.Unavailable(
                "No repositories are configured. Add one in Settings and its pull requests and issues appear here.");
        }

        return InsightAvailability.Available;
    }

    public async Task<ActivityReport> GetActivityAsync(
        IReadOnlyList<DashboardRepository> repositories,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repositories);

        var login = await identity.GetLoginAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(login)) return ActivityReport.Empty;

        var pullRequests = new List<ActivityPullRequest>();
        var issues = new List<ActivityIssue>();

        foreach (var repository in repositories)
        {
            var reference = settings.Current.Find(repository.Alias);

            // A scope naming a repository that Settings no longer has narrows to
            // nothing for that entry rather than widening to another one.
            if (reference is null) continue;

            GitHubRepositoryActivity report;
            try
            {
                report = await activity
                    .GetActivityAsync(reference, from, to, login, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (GitHubException)
            {
                continue;
            }
            catch (GitHubNotConfiguredException)
            {
                continue;
            }

            pullRequests.AddRange(report.PullRequests.Select(pr => new ActivityPullRequest(
                repository.Alias,
                pr.Number,
                pr.MergedAt,
                pr.FirstReviewedAt,
                pr.ReviewRounds,
                pr.CommitsAfterFirstReview,
                pr.ForcePushesAfterFirstReview,
                pr.FilesRetouched,
                pr.ChurnComplete)
            {
                ReviewTurnaround = pr.ReviewTurnaround
            }));

            issues.AddRange(report.Issues.Select(issue =>
                new ActivityIssue(repository.Alias, issue.Number, issue.ClosedAt)));
        }

        return new ActivityReport(pullRequests, issues);
    }
}
