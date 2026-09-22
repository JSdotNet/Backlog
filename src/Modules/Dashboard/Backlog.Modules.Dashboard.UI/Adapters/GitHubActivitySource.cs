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
/// Resolved per repository, not once. A repository bound to an account in Settings
/// has its calls sent as that account, and the work in it is authored by that
/// account's login — so filtering it to the login the default account answers for
/// authenticated correctly and then dropped every pull request it found. The
/// author follows the same choice the transport makes: the bound login when there
/// is one, the signed-in login otherwise.
/// </para>
/// <para>
/// A repository that fails is skipped rather than failing the fetch. Five
/// repositories where one has been renamed should show four repositories' figures
/// and not an unavailable part, because the four are true — but the report says
/// it is incomplete, because the fifth is missing from it and every figure on
/// it is a floor.
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

        // Every repository's honesty flags fold into one. A repository that was
        // skipped for an error counts against it too: the four that answered are
        // true, but a report headed "across every configured repository" that
        // silently lacks the fifth is a floor, and the flag is the one thing on
        // screen that says so. The parts cache a successful report for the whole
        // session, so a refusal folded in as complete would stay complete until
        // the next Refresh.
        var complete = true;

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
                    .GetActivityAsync(reference, from, to, AuthorOf(reference, login), cancellationToken)
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

            complete &= report.ListingComplete && report.DetailComplete;

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
                Title = pr.Title,
                ReviewTurnaround = pr.ReviewTurnaround,
                ChangedLines = pr.ChangedLines,
                ChangedFiles = pr.ChangedFiles,
                SizeKnown = pr.SizeKnown,
                Commits = pr.Commits,
                ChangesRequested = pr.ChangesRequested,
                SyncMerges = pr.SyncMerges,
                ConflictedSyncMerges = pr.ConflictedSyncMerges,
                SyncsKnown = pr.SyncsKnown
            }));

            issues.AddRange(report.Issues.Select(issue =>
                new ActivityIssue(repository.Alias, issue.Number, issue.ClosedAt) { Title = issue.Title }));
        }

        return new ActivityReport(pullRequests, issues) { Complete = complete };
    }

    /// <summary>
    /// The login one repository's work is filtered to: the account it is bound to,
    /// or the signed-in login when it is not bound.
    /// <para>
    /// Asked of the same lookup the transport routes the call with, so the author
    /// and the credential cannot disagree — a binding this machine cannot satisfy
    /// still names its login here, and the transport's refusal is what takes the
    /// repository off the report, as it does for any other error.
    /// </para>
    /// </summary>
    internal static string AuthorOf(GitHubSettings settings, GitHubRepositoryRef repository, string signedIn) =>
        settings.AccountForPath($"repos/{repository.Owner}/{repository.Name}/pulls").Login ?? signedIn;

    private string AuthorOf(GitHubRepositoryRef repository, string signedIn) =>
        AuthorOf(settings.Current, repository, signedIn);
}
