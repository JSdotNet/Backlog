using System.Globalization;
using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.Services;

/// <summary>
/// The Needs-you items: four rules, each over its tab's own insight, in a fixed order.
/// </summary>
/// <remarks>
/// <para>
/// Every rule reads an insight the dashboard already has rather than a source of its own,
/// so an item never says something the tab it links to does not show. The reads run
/// side by side; each insight keeps its own cache, so the tab that opens next reads what
/// this read.
/// </para>
/// <para>
/// A source that cannot answer — unavailable, or throwing — is skipped and its reason kept
/// in <see cref="NeedsYouInsight.Skipped"/>. The list is never an error: one unconfigured
/// provider must not hide the drift. The caller's own cancellation is the exception and is let through; a source timing out on its own is skipped.
/// </para>
/// </remarks>
internal sealed class AttentionInsights(
    IDriftInsights drift,
    ITaskInsights tasks,
    ICostInsights cost,
    IProductivityInsights productivity) : IAttentionInsights
{
    internal const string ConflictVerdict = "conflict";

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public async Task<NeedsYouInsight> GetNeedsYouAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var driftRead = ReadAsync("Drift", () => drift.GetDriftAsync(scope, cancellationToken), cancellationToken);
        var planRead = ReadAsync("The roadmap", () => tasks.GetPlanAsync(scope, cancellationToken), cancellationToken);
        var projectionRead = ReadAsync("The month-end projection", () => cost.GetProjectionAsync(cancellationToken), cancellationToken);
        var reworkRead = ReadAsync("Rework", () => productivity.GetReworkAsync(scope, cancellationToken), cancellationToken);

        await Task.WhenAll(driftRead, planRead, projectionRead, reworkRead).ConfigureAwait(false);

        var items = new List<NeedsYouItem>();
        var skipped = new List<string>();

        var driftResult = await driftRead;
        Apply(driftResult, DriftItems, items, skipped);

        if (driftResult.Value?.IssuesNote is { } issuesNote)
        {
            skipped.Add(issuesNote);
        }

        Apply(await planRead, BehindItems, items, skipped);
        Apply(await projectionRead, PastBudgetItems, items, skipped);
        Apply(await reworkRead, ReworkItems, items, skipped);

        return new NeedsYouInsight(items, skipped);
    }

    /// <summary>Rule 1: each unit whose drift issue carries <c>sync-failed</c>, or whose
    /// last verdict is <c>conflict</c> — and a <c>sync-failed</c> issue whose unit no
    /// verdict names, so a GitHub that answers alone still raises it.</summary>
    internal static IEnumerable<NeedsYouItem> DriftItems(DriftInsight insight)
    {
        var raised = new HashSet<(string, string)>();

        foreach (var unit in insight.Directions.SelectMany(direction => direction.Units))
        {
            var conflict = string.Equals(unit.Verdict?.Trim(), ConflictVerdict, StringComparison.OrdinalIgnoreCase);

            if (!unit.SyncFailed && !conflict) continue;

            raised.Add(Key(unit.RepositoryAlias, unit.Unit));

            yield return unit.SyncFailed
                ? SyncFailed(unit.RepositoryAlias, unit.Unit)
                : new NeedsYouItem(
                    NeedsYouRule.Drift,
                    "Devbook conflict: " + unit.Unit,
                    $"In {unit.RepositoryAlias}, the last sweep's verdict is conflict: the chapter and the code disagree and a person decides which is right.",
                    NeedsYouTab.Devbook);
        }

        foreach (var issue in insight.SyncFailed)
        {
            var reference = Named(issue.Title);

            if (!raised.Add(Key(issue.RepositoryAlias, reference))) continue;

            yield return SyncFailed(issue.RepositoryAlias, reference);
        }
    }

    /// <summary>Rule 2: each roadmap item behind pace — projected past its end, or
    /// already past it with work open, the one definition the Overview tile, the brief
    /// and the Tasks tab count — with the estimated points it has left, and how many of
    /// its things carry no estimate, so the points read as a floor when they are one.</summary>
    internal static IEnumerable<NeedsYouItem> BehindItems(PlanInsight insight)
    {
        if (!insight.RoadmapEnabled) yield break;

        foreach (var planned in insight.Items.Where(item => item.Outlook is PlanOutlook.Behind or PlanOutlook.Overdue))
        {
            var left = Math.Max(0, planned.Item.TotalEffort - planned.Item.DoneEffort);
            var unestimated = planned.Item.Unestimated > 0
                ? $" (and {planned.Item.Unestimated} unestimated)"
                : string.Empty;
            var lands = planned.ProjectedEnd is { } end
                ? $", projected to land {Day(end)} against an end of {Day(planned.Item.End)}."
                : $", against an end of {Day(planned.Item.End)}.";

            var state = planned.Outlook == PlanOutlook.Overdue ? "Past its end" : "Behind";

            yield return new NeedsYouItem(
                NeedsYouRule.RoadmapBehind,
                planned.Item.Title,
                $"{state}: {Points(left)}{unestimated} left{lands}",
                NeedsYouTab.Tasks);
        }
    }

    /// <summary>Rule 3: each provider whose month-end projection is above its monthly
    /// budget, with the day the projection crosses it.</summary>
    internal static IEnumerable<NeedsYouItem> PastBudgetItems(SpendProjectionInsight insight)
    {
        foreach (var projection in insight.Providers.Where(projection => projection.IsProjectedPastBudget))
        {
            var budget = projection.Budget!;
            var crosses = CrossingDay(projection);
            var when = crosses is { } day
                ? $"Crosses its {Money(budget)} budget on {Day(day)}"
                : $"Already past its {Money(budget)} budget";

            yield return new NeedsYouItem(
                NeedsYouRule.PastBudget,
                $"{projection.Provider.DisplayName()} is projected past its budget",
                $"{when}; projected {Money(projection.Projected)} by the month's end.",
                NeedsYouTab.Cost);
        }
    }

    /// <summary>Rule 4: rework, when the pull requests that came back after review are
    /// more than a quarter of those reviewed in the window — the Productivity tab's rework
    /// rate, over the same denominator, so the two never disagree. Exactly a quarter is
    /// not; nothing reviewed raises nothing. The detail splits the count by repository,
    /// leaving out a repository nothing came back from.</summary>
    internal static IEnumerable<NeedsYouItem> ReworkItems(ReworkInsight rework)
    {
        var reviewed = rework.PullRequestsReviewed;
        var cameBack = rework.PullRequestsWithChurn;

        if (reviewed <= 0 || (decimal)cameBack / reviewed <= NeedsYouInsight.ReworkThreshold) yield break;

        var title = cameBack == 1
            ? "1 pull request came back after review"
            : $"{cameBack} pull requests came back after review";

        var split = rework.ByRepository
            .Where(row => row.PullRequestsWithChurn > 0)
            .Select(row => $"{row.PullRequestsWithChurn} in {row.Alias}")
            .ToList();

        var detail = split.Count == 0
            ? $"{cameBack} of {reviewed} reviewed came back."
            : $"{cameBack} of {reviewed} reviewed came back: {string.Join(", ", split)}.";

        yield return new NeedsYouItem(NeedsYouRule.Rework, title, detail, NeedsYouTab.Productivity);
    }

    /// <summary>
    /// The first day the projection's running total is above the budget: the month-to-date
    /// figure through <see cref="SpendProjection.Through"/>, plus the average daily spend
    /// for each day after it, never past the month's last day. Null when the month-to-date
    /// figure is past it already.
    /// </summary>
    internal static DateOnly? CrossingDay(SpendProjection projection)
    {
        var budget = projection.Budget!.Amount;
        var spent = projection.MonthToDate.Amount;

        if (spent > budget) return null;

        var daily = projection.AverageDaily.Amount;
        if (daily <= 0m) return null;

        var days = (int)decimal.Floor((budget - spent) / daily) + 1;

        return projection.Through.AddDays(Math.Min(days, Math.Max(projection.DaysLeft, 1)));
    }

    private static NeedsYouItem SyncFailed(string repository, string unit) =>
        new(
            NeedsYouRule.Drift,
            "Devbook sync failed: " + unit,
            $"In {repository}, its drift issue carries sync-failed: no sweep retries it until a person clears the label.",
            NeedsYouTab.Devbook);

    private static void Apply<T>(
        (T? Value, string? Skipped) read,
        Func<T, IEnumerable<NeedsYouItem>> rule,
        List<NeedsYouItem> items,
        List<string> skipped)
    {
        if (read.Skipped is { } why)
        {
            skipped.Add(why);
            return;
        }

        items.AddRange(rule(read.Value!));
    }

    private static async Task<(T? Value, string? Skipped)> ReadAsync<T>(
        string source,
        Func<Task<InsightResult<T>>> read,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await read().ConfigureAwait(false);

            return result.HasValue
                ? (result.Value, null)
                : (default, Skip(source, result.Availability.Reason));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A source's own timeout surfaces as an OperationCanceledException with the
            // caller's token still live: that source is skipped like any other failure.
            // Only the caller's own cancellation is let through.
            return (default, Skip(source, exception.Message));
        }
    }

    /// <summary>
    /// Why a source was skipped, naming it once. Most insights' reasons already open with
    /// the source — "Drift is unavailable: …", "The roadmap could not be read: …" — and
    /// are kept as they are; any other reason gets the source in front.
    /// </summary>
    internal static string Skip(string source, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return $"{source} could not be read.";

        var first = DriftInsights.Reason(reason);

        return first.StartsWith(source, StringComparison.OrdinalIgnoreCase)
            ? first
            : $"{source} could not be read: {first}";
    }

    /// <summary>The unit or chapter a drift issue's title names, as the title spells it.</summary>
    private static string Named(string title)
    {
        var trimmed = title.Trim();

        return trimmed.StartsWith(DriftInsights.IssueTitlePrefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[DriftInsights.IssueTitlePrefix.Length..].Trim()
            : trimmed;
    }

    private static (string, string) Key(string repository, string unit) =>
        (repository.Trim().ToLowerInvariant(), unit.Trim().ToLowerInvariant());

    private static string Day(DateOnly day) => day.ToString("d MMM", Culture);

    private static string Points(int points) => points == 1 ? "1 point" : $"{points} points";

    private static string Money(DashboardMoney money) => money.Format();
}
