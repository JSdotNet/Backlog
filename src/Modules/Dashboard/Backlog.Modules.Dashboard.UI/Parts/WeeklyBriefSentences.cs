using System.Globalization;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.UI.Components.Metrics;

namespace Backlog.Modules.Dashboard.UI.Parts;

/// <summary>
/// The weekly brief's sentences: fixed templates filled from the figures the tabs read.
/// </summary>
/// <remarks>
/// <para>
/// Templates and never a model. A sentence here says exactly what the figure under it
/// says, in the same words every week, so two briefs a month apart can be read against
/// each other and none of them can claim what its tab does not show.
/// </para>
/// <para>
/// A headline is a figure and its comparison with the week before: <c>23 pull requests
/// merged — 4 more than last week.</c> The comparison has three shapes — more, fewer or
/// as many — and it is left off when there is no earlier figure, rather than drawn
/// against a zero that would read as a quiet week. A count of one is singular.
/// </para>
/// <para>
/// Invariant, for the reason every figure on this surface is: a sentence that moved with
/// the machine's language would make a screenshot untranslatable back to its data.
/// </para>
/// </remarks>
public static class WeeklyBriefSentences
{
    /// <summary>The Shipping block's headline.</summary>
    public static string Merged(int merged, int? previous) =>
        Headline(
            Count(merged, "pull request merged", "pull requests merged"),
            CountComparison(merged, previous));

    /// <summary>The Plan block's headline.</summary>
    public static string PointsDone(decimal points, decimal? previous) =>
        Headline(
            Count(points, "story point done", "story points done"),
            CountComparison(points, previous));

    /// <summary>The Sessions block's headline, in hours to a tenth.</summary>
    public static string AgentTime(decimal hours, decimal? previous) =>
        Headline(
            $"{DashboardFormat.Hours(hours)} of agent time",
            AmountComparison(hours, previous, DashboardFormat.Hours));

    /// <summary>The Cost block's headline. The comparison is drawn only in the same
    /// currency: there is no exchange rate here to compare two with.</summary>
    public static string Spent(DashboardMoney spent, DashboardMoney? previous)
    {
        ArgumentNullException.ThrowIfNull(spent);

        var comparable = previous is not null
            && string.Equals(previous.Currency, spent.Currency, StringComparison.OrdinalIgnoreCase)
                ? previous.Amount
                : (decimal?)null;

        return Headline(
            $"{Money(spent.Amount, spent.Currency)} spent",
            AmountComparison(spent.Amount, comparable, amount => Money(amount, spent.Currency)));
    }

    /// <summary>The Shipping block's supporting sentence: the issues closed the same
    /// week, and the median wait for a first review when a merge that week had one.</summary>
    public static string ShippingDetail(int issuesClosed, decimal? medianReviewHours)
    {
        var issues = Count(issuesClosed, "issue", "issues") + " closed";

        return medianReviewHours is > 0m
            ? $"{issues}, and a first review took a median of {DashboardFormat.Hours(medianReviewHours.Value)}."
            : $"{issues}.";
    }

    /// <summary>The Plan block's supporting sentence, from the roadmap the Tasks tab
    /// reads: how many of its items in view are behind pace. Null when the roadmap could
    /// not be read; switched off says so.</summary>
    public static string PlanDetail(PlanInsight? plan)
    {
        if (plan is null) return "The roadmap could not be read.";
        if (!plan.RoadmapEnabled) return "The roadmap is switched off.";
        if (plan.Items.Count == 0) return "No roadmap item falls in the window.";

        // Behind is the outlook the Overview's tile and Needs you count, so the brief
        // counts the same; an item already past its end is said apart, never as on pace.
        var behind = plan.Items.Count(item => item.Outlook == PlanOutlook.Behind);
        var overdue = plan.Items.Count(item => item.Outlook == PlanOutlook.Overdue);
        var done = $"with {Count(plan.DoneEffort, "point", "points")} of {plan.PlannedEffort} done";

        return (behind, overdue) switch
        {
            (> 0, _) => $"{Count(behind, "roadmap item is", "roadmap items are")} behind pace, {done}.",
            (0, > 0) => $"{Count(overdue, "roadmap item is past its end", "roadmap items are past their end")} with work open, {done}.",
            _ => $"Every roadmap item in view is on pace, {done}."
        };
    }

    /// <summary>The Sessions block's supporting sentence.</summary>
    public static string SessionsDetail(int sessions) =>
        $"{Count(sessions, "session was", "sessions were")} last active that week.";

    /// <summary>The Cost block's supporting sentence, from the month-end projection the
    /// Cost tab reads. Null when the projection could not be read.</summary>
    public static string CostDetail(SpendProjectionInsight? projection)
    {
        if (projection is null) return "This month's projection could not be read.";

        var budgeted = projection.Providers.Where(provider => provider.Budget is not null).ToList();
        if (budgeted.Count == 0) return "No monthly budget is set.";

        var passing = budgeted.Where(provider => provider.IsProjectedPastBudget).ToList();

        return passing.Count switch
        {
            0 => "Every budgeted provider is on course to stay within budget this month.",
            1 => $"{Name(passing[0].Provider)} is on course to pass its budget this month.",
            _ => $"{passing.Count} providers are on course to pass their budgets this month."
        };
    }

    /// <summary>What a block says in one line, instead of its sentences, when its source
    /// could not answer.</summary>
    public static string Missing(string block, string reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? $"No {block.ToLowerInvariant()} figures for this week."
            : $"No {block.ToLowerInvariant()} figures for this week: {reason.Trim()}";

    /// <summary>A count with its noun, singular for one: <c>1 pull request merged</c>.</summary>
    public static string Count(decimal count, string singular, string plural) =>
        $"{MetricFormat.Compact(count)} {(count == 1m ? singular : plural)}";

    private static string Headline(string figure, string? comparison) =>
        comparison is null ? $"{figure}." : $"{figure} — {comparison}.";

    private static string? CountComparison(decimal current, decimal? previous)
    {
        if (previous is not { } before) return null;

        var difference = current - before;

        return difference switch
        {
            > 0 => $"{MetricFormat.Compact(difference)} more than last week",
            < 0 => $"{MetricFormat.Compact(-difference)} fewer than last week",
            _ => "as many as last week"
        };
    }

    private static string? AmountComparison(decimal current, decimal? previous, Func<decimal, string> format)
    {
        if (previous is not { } before) return null;

        var difference = current - before;

        return difference switch
        {
            > 0 => $"{format(difference)} more than last week",
            < 0 => $"{format(-difference)} less than last week",
            _ => "as much as last week"
        };
    }

    private static string Money(decimal amount, string currency) =>
        MetricFormat.Money(new MoneyAmount(amount, currency));

    private static string Name(SpendProvider provider) => provider switch
    {
        SpendProvider.Claude => "Claude",
        SpendProvider.Copilot => "Copilot",
        SpendProvider.AzureFoundry => "Azure Foundry",
        _ => provider.ToString()
    };

    /// <summary>The week a brief covers as its eyebrow says it: <c>Week 41 · 05 Oct – 11 Oct</c>.</summary>
    public static string WeekLabel(BriefWeek week)
    {
        ArgumentNullException.ThrowIfNull(week);

        return $"Week {week.Number.ToString(CultureInfo.InvariantCulture)} · {DashboardFormat.Span(week.Monday, week.Sunday)}";
    }
}
