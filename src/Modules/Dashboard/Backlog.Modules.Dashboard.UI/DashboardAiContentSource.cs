using System.Globalization;
using System.Text;
using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.SharedKernel.Ai;

namespace Backlog.Modules.Dashboard.UI;

/// <summary>
/// The Dashboard's answer to <see cref="IAiContentSource"/>: the GitHub facts the
/// overview is drawn from — the reader's merged pull requests and closed issues
/// in the repositories and window on screen.
/// </summary>
/// <remarks>
/// <para>
/// The facts and not the figures. A productivity score is the dashboard's
/// reading of the pull requests; sending the reading would have the assistant
/// explain a number it cannot see behind. Sending the pull requests lets it
/// answer "which of these took longest to review" from the same rows the score
/// was worked out from.
/// </para>
/// <para>
/// Fetched through <see cref="IActivitySource"/> for the scope the pane
/// published — the same repositories, the same four or twelve weeks — the way
/// the module's own derivations do, so the body describes what the reader is
/// looking at and not every repository over every window. The machine focus is
/// not applied, for the reason the parts say on screen: GitHub does not record
/// which machine a pull request was worked from.
/// </para>
/// <para>
/// Newest first, before relevance ranks them, so two rows the question says
/// nothing about fall to the recent one — a question asked on a dashboard is
/// usually about what just happened.
/// </para>
/// </remarks>
internal sealed class DashboardAiContentSource(
    IActivitySource activity,
    IRepositoryDirectory repositories,
    DashboardScopeInView scope,
    TimeProvider time) : IAiContentSource
{
    public string AreaKey => "dashboard";

    public string AreaTitle => "Dashboard";

    public async Task<AiContent> ComposeAsync(AiContentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var availability = await activity.GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);

        // A dashboard with no provider behind it says so on every part; the
        // body says the same, so the reader knows the answer will come from
        // nothing before they send the question.
        if (!availability.IsAvailable)
        {
            return new AiContent(AreaKey, $"{AreaTitle}: unavailable. {availability.Reason}", 0, 0, false);
        }

        var current = scope.Current;
        var (from, to) = current.Window(time.GetUtcNow());
        var scoped = current.IsAllRepositories
            ? repositories.Repositories
            : [.. repositories.Repositories.Where(repository => current.Repositories.Contains(repository.Alias))];

        var report = await activity.GetActivityAsync(scoped, from, to, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<string> records =
        [
            .. report.PullRequests.OrderByDescending(pr => pr.MergedAt).Select(Text),
            .. report.Issues.OrderByDescending(issue => issue.ClosedAt).Select(Text)
        ];

        // The scope in the title: it is what defines which facts this area has,
        // so the first line says it. An incomplete report says so on the next
        // line, because every count the assistant makes from it is a floor.
        return AiContentBudget.Compose(
            AreaKey,
            $"{AreaTitle} ({current.Weeks} weeks, {current.Repositories})",
            records,
            record => record,
            request.Question,
            request.BudgetCharacters,
            note: report.Complete ? null : "One or more repositories could not be read.");
    }

    /// <summary>"PR #n: title — merged, repo, date, review detail, size."</summary>
    internal static string Text(ActivityPullRequest pr)
    {
        var text = new StringBuilder("PR #").Append(pr.Number.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(pr.Title)) text.Append(": ").Append(pr.Title.Trim());

        text.Append(" — merged, ").Append(pr.RepositoryAlias).Append(", ").Append(Date(pr.MergedAt));

        text.Append(pr.FirstReviewedAt is null
            ? ", not reviewed"
            : $", {pr.ReviewRounds} review round{(pr.ReviewRounds == 1 ? "" : "s")}");

        if (pr.HasChurn) text.Append(", reworked after review");
        if (pr.SizeKnown) text.Append($", {pr.ChangedLines} lines in {pr.ChangedFiles} files");

        return text.ToString();
    }

    /// <summary>"Issue #n: title — closed, repo, date."</summary>
    internal static string Text(ActivityIssue issue)
    {
        var text = new StringBuilder("Issue #").Append(issue.Number.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(issue.Title)) text.Append(": ").Append(issue.Title.Trim());

        text.Append(" — closed, ").Append(issue.RepositoryAlias).Append(", ").Append(Date(issue.ClosedAt));

        return text.ToString();
    }

    private static string Date(DateTimeOffset at) => AiContentBudget.Date(at);
}
