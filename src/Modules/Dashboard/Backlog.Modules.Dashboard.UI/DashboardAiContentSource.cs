using System.Globalization;
using System.Text;
using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.SharedKernel.Ai;

namespace Backlog.Modules.Dashboard.UI;

/// <summary>
/// The Dashboard's answer to <see cref="IAiContentSource"/>: the figures every tab
/// draws, then the GitHub facts behind them — the reader's merged pull requests and
/// closed issues in the repositories and window on screen.
/// </summary>
/// <remarks>
/// <para>
/// The figures and the facts. This used to send the facts alone, on the reasoning
/// that a score is the dashboard's reading of the pull requests and the assistant
/// should see what is behind it rather than a number it cannot explain. That holds
/// for the pull requests, which still go; but it left a question about a chart —
/// "why is my rework up", "am I on pace" — with nothing to answer from, because most
/// of what the tabs draw is not in any pull request: the plan, the sessions, the
/// hours, the spend. So each insight a part reads is one line here
/// (<see cref="DashboardAiFigures"/>), worded the way its part words it.
/// </para>
/// <para>
/// Every tab, not the one on show. Which tab is open is screen state, and the
/// <see cref="IAiContentSource"/> contract is that a source composes its area's
/// content and not what the screen is showing of it — the Overview reads across the
/// tabs anyway. A per-part "Ask AI" names its chart in the question instead, which
/// is the reader's own words and no breach of that contract.
/// </para>
/// <para>
/// The figures go first and always fit: they are handed to
/// <see cref="AiContentBudget.Select{T}"/> as pinned records, so they are charged
/// before any pull request and no question's words can rank one out. The pull
/// requests fill what is left, ranked against the question as before. A dozen
/// short lines is a small, fixed price for every question, and it is the price of
/// the dashboard's own figures never being the thing that was left out. The first
/// line counts only the pull requests and issues as entries, and the line under it
/// says the figures are not — see <see cref="Compose"/>.
/// </para>
/// <para>
/// An insight that cannot answer is a line saying so, in its place, with its
/// reason — never a missing line, which the assistant would read as nothing to
/// report, and never a failed body, which would take every other figure with it.
/// GitHub being unavailable is the same rule a level up: the pull requests are not
/// fetched and a note says so, but tasks, sessions, hours and spend do not come from
/// GitHub and are still sent.
/// </para>
/// <para>
/// Everything is read for the scope the pane published through
/// <see cref="DashboardScopeInView"/> — the same repositories, the same four to
/// twenty-six weeks. The figures are read through the same scoped insights the parts
/// inject, so a figure here comes out of the cache the part on screen filled and
/// cannot disagree with it; the machine focus reaches them as it reaches the parts.
/// Spend is the exception the Cost tab already is: no provider reports it per
/// repository or machine and it is read for the calendar month, so its lines name
/// the month.
/// </para>
/// <para>
/// The pull requests and issues are not the insights' — they are fetched here,
/// straight from <see cref="IActivitySource"/>, for the same repositories and window,
/// and nothing caches that fetch. The machine focus is not applied to them, for the
/// reason the parts say on screen: GitHub does not record which machine a pull
/// request was worked from.
/// </para>
/// <para>
/// The weekly brief's sentences are not sent. They are written from these same
/// insights, so they would be every figure a second time in longer words.
/// </para>
/// <para>
/// Pull requests go newest first, before relevance ranks them, so two rows the
/// question says nothing about fall to the recent one — a question asked on a
/// dashboard is usually about what just happened.
/// </para>
/// </remarks>
internal sealed class DashboardAiContentSource(
    IActivitySource activity,
    IRepositoryDirectory repositories,
    DashboardScopeInView scope,
    DashboardInsightPorts insights,
    TimeProvider time) : IAiContentSource
{
    public string AreaKey => "dashboard";

    public string AreaTitle => "Dashboard";

    public async Task<AiContent> ComposeAsync(AiContentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var current = scope.Current;

        // The figures and the GitHub read side by side: every insight keeps its own
        // cache, which joins concurrent callers to one read, so this costs what the
        // slowest of them costs — usually nothing, because the parts asked first.
        var figures = ComposeFiguresAsync(current, cancellationToken);
        var availability = await activity.GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<DashboardRecord> facts = [];
        string? note;

        if (!availability.IsAvailable)
        {
            // A dashboard with no GitHub behind it says so on every productivity
            // part; the body says the same, so the reader knows the answer cannot
            // name a pull request before they send the question.
            note = $"GitHub is unavailable: {availability.Reason} No pull request or issue is included.";
        }
        else
        {
            var (from, to) = current.Window(time.GetUtcNow());
            var scoped = current.IsAllRepositories
                ? repositories.Repositories
                : [.. repositories.Repositories.Where(repository => current.Repositories.Contains(repository.Alias))];

            var report = await activity.GetActivityAsync(scoped, from, to, cancellationToken).ConfigureAwait(false);

            facts =
            [
                .. report.PullRequests.OrderByDescending(pr => pr.MergedAt).Select(pr => new DashboardRecord(Text(pr), false)),
                .. report.Issues.OrderByDescending(issue => issue.ClosedAt).Select(issue => new DashboardRecord(Text(issue), false))
            ];

            // An incomplete report says so under the first line, because every
            // count the assistant makes from it is a floor.
            note = report.Complete ? null : "One or more repositories could not be read.";
        }

        IReadOnlyList<DashboardRecord> records = [.. await figures.ConfigureAwait(false), .. facts];

        return Compose($"{AreaTitle} ({current.Weeks} weeks, {current.Repositories})", records, facts.Count, note, request);
    }

    /// <summary>
    /// <see cref="AiContentBudget.Compose{T}"/>'s steps, taken one by one so the first
    /// line counts the pull requests and issues and not the figures.
    /// <para>
    /// The header's count is what stops the assistant counting the entries in front of
    /// it and reporting that as the total, so it has to be a count of something the
    /// assistant would count: "20 of 61 entries" with eleven of the 61 being figure
    /// lines would have it say 61 pull requests. The figures are named on a line of
    /// their own under the header instead. Same budget rule as the shared call: every
    /// line above the records is reserved before any record is chosen, at its longest
    /// form, so the body never runs past the budget.
    /// </para>
    /// </summary>
    private AiContent Compose(string title, IReadOnlyList<DashboardRecord> records, int facts, string? note, AiContentRequest request)
    {
        var figureCount = records.Count - facts;
        var reserved = AiContentBudget.HeaderReserve(title, facts)
            + FiguresNote(figureCount).Length + 1
            + (note is null ? 0 : note.Length + 1);

        var selection = AiContentBudget.Select(records, record => record.Text, request.Question, request.BudgetCharacters - reserved, record => record.Figure);
        var shownFacts = selection.Selected.Count(record => !record.Figure);
        var shownFigures = selection.Selected.Count - shownFacts;
        var trimmed = shownFacts < facts;

        var body = AiContentBudget.Render(title, selection.Selected.Select(record => record.Text), shownFacts, facts, trimmed);

        // Under the first line, in the order they are read: what the first lines of
        // the body are, then what is missing from the rest of it.
        var under = note is null ? FiguresNote(shownFigures) : FiguresNote(shownFigures) + "\n" + note;
        var lineBreak = body.IndexOf('\n');
        body = lineBreak < 0 ? body + "\n" + under : body.Insert(lineBreak, "\n" + under);

        return new AiContent(AreaKey, body, shownFacts, facts, trimmed);
    }

    /// <summary>The line that says the figure lines are not entries. The count is
    /// the lines actually sent, so a budget too small for all of them is not
    /// described as holding them.</summary>
    private static string FiguresNote(int figures) =>
        $"First, {figures.ToString(CultureInfo.InvariantCulture)} lines of the dashboard's figures, one per part across every tab; they are not entries. Any entries after them are merged pull requests and closed issues.";

    /// <summary>
    /// One line per insight, in the tabs' order — Productivity, Tasks, Sessions,
    /// Devbook, Cost, then the Overview's Needs you — so two questions see the
    /// figures the same way round.
    /// </summary>
    private async Task<IReadOnlyList<DashboardRecord>> ComposeFiguresAsync(DashboardScope current, CancellationToken cancellationToken)
    {
        var headline = insights.Productivity.GetHeadlineAsync(current, cancellationToken);
        var score = insights.Productivity.GetScoreAsync(current, cancellationToken);
        var rework = insights.Productivity.GetReworkAsync(current, cancellationToken);
        var throughput = insights.Tasks.GetThroughputAsync(current, cancellationToken);
        var plan = insights.Tasks.GetPlanAsync(current, cancellationToken);
        var sessions = insights.Sessions.GetSessionsAsync(current, cancellationToken);
        var hours = insights.HoursWorked.GetHoursWorkedAsync(current, cancellationToken);
        var drift = insights.Drift.GetDriftAsync(current, cancellationToken);
        var projection = insights.Cost.GetProjectionAsync(cancellationToken);
        var byModel = insights.Cost.GetByModelAsync(cancellationToken);
        var needsYou = insights.Attention.GetNeedsYouAsync(current, cancellationToken);

        // The calendar month spend is read for, named the way the Cost tab's header
        // names it — on the local clock, because a month is a person's.
        var month = time.GetLocalNow().ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        string[] lines =
        [
            Line(await headline.ConfigureAwait(false), "Throughput", value => DashboardAiFigures.Throughput(value, current)),
            Line(await score.ConfigureAwait(false), "Score", DashboardAiFigures.Score),
            Line(await rework.ConfigureAwait(false), "Rework after review", DashboardAiFigures.Rework),
            Line(await throughput.ConfigureAwait(false), "Tasks", value => DashboardAiFigures.Tasks(value, current)),
            Line(await plan.ConfigureAwait(false), "Plan", DashboardAiFigures.Plan),
            Line(await sessions.ConfigureAwait(false), "Assistant sessions", DashboardAiFigures.Sessions),
            Line(await hours.ConfigureAwait(false), "Hours worked", DashboardAiFigures.HoursWorked),
            Line(await drift.ConfigureAwait(false), "Devbook drift", DashboardAiFigures.Drift),
            Line(await projection.ConfigureAwait(false), DashboardAiFigures.SpendThisMonthName(month), value => DashboardAiFigures.SpendThisMonth(value, month)),
            Line(await byModel.ConfigureAwait(false), DashboardAiFigures.SpendByModelName(month), value => DashboardAiFigures.SpendByModel(value, month)),

            // Not an InsightResult: Needs you is never unavailable as a whole — a rule
            // whose source cannot answer is listed as skipped inside it.
            DashboardAiFigures.NeedsYou(await needsYou.ConfigureAwait(false))
        ];

        return [.. lines.Select(line => new DashboardRecord(line, true))];
    }

    /// <summary>The insight's line, or the line saying why there is none.</summary>
    private static string Line<T>(InsightResult<T> result, string name, Func<T, string> text) =>
        result.HasValue ? text(result.Value!) : DashboardAiFigures.Unavailable(name, result.Availability);

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

    /// <summary>One line of the body: a figure, which is pinned, or a pull request
    /// or issue, which competes for what is left.</summary>
    private sealed record DashboardRecord(string Text, bool Figure);
}
