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
/// so an item never says something the tab it links to does not show. The four reads run
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
        var headlineRead = ReadAsync("Rework", () => productivity.GetHeadlineAsync(scope, cancellationToken), cancellationToken);
        var reworkRead = ReadAsync("Rework", () => productivity.GetReworkAsync(scope, cancellationToken), cancellationToken);

        await Task.WhenAll(driftRead, planRead, projectionRead, headlineRead, reworkRead).ConfigureAwait(false);

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

        var (headline, headlineSkipped) = await headlineRead;
        var (rework, reworkSkipped) = await reworkRead;

        if ((headlineSkipped ?? reworkSkipped) is { } why)
        {
            skipped.Add(why);
        }
        else
        {
            items.AddRange(ReworkItems(headline!, rework!));
        }

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

    /// <summary>Rule 2: each roadmap item whose outlook is Behind, with the estimated
    /// points it has left — and how many of its things carry no estimate, so the points
    /// read as a floor when they are one.</summary>
    internal static IEnumerable<NeedsYouItem> BehindItems(PlanInsight insight)
    {
        if (!insight.RoadmapEnabled) yield break;

        foreach (var planned in insight.Items.Where(item => item.Outlook == PlanOutlook.Behind))
        {
            var left = Math.Max(0, planned.Item.TotalEffort - planned.Item.DoneEffort);
            var unestimated = planned.Item.Unestimated > 0
                ? $" (and {planned.Item.Unestimated} unestimated)"
                : string.Empty;
            var lands = planned.ProjectedEnd is { } end
                ? $", projected to land {Day(end)} against an end of {Day(planned.Item.End)}."
                : $", against an end of {Day(planned.Item.End)}.";

            yield return new NeedsYouItem(
                NeedsYouRule.RoadmapBehind,
                planned.Item.Title,
                $"Behind: {Points(left)}{unestimated} left{lands}",
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
                $"{Name(projection.Provider)} is projected past its budget",
                $"{when}; projected {Money(projection.Projected)} by the month's end.",
                NeedsYouTab.Cost);
        }
    }

    /// <summary>Rule 4: rework, when the pull requests that came back after review are
    /// more than a quarter of those merged in the window. Exactly a quarter is not.</summary>
    internal static IEnumerable<NeedsYouItem> ReworkItems(ProductivityHeadline headline, ReworkInsight rework)
    {
        var merged = headline.PullRequestsMerged;
        var cameBack = rework.PullRequestsWithChurn;

        if (merged <= 0 || (decimal)cameBack / merged <= NeedsYouInsight.ReworkThreshold) yield break;

        var share = ((decimal)cameBack / merged).ToString("0%", Culture);

        yield return new NeedsYouItem(
            NeedsYouRule.Rework,
            "Rework after review",
            $"{cameBack} of {merged} merged pull requests came back after review ({share}).",
            NeedsYouTab.Productivity);
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

    private static string Money(DashboardMoney money) =>
        $"{money.Amount.ToString("0.00", Culture)} {money.Currency}";

    private static string Name(SpendProvider provider) => provider switch
    {
        SpendProvider.AzureFoundry => "Azure AI Foundry",
        _ => provider.ToString()
    };
}
