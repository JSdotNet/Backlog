using System.Globalization;
using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.UI.Components.Metrics;

namespace Backlog.Modules.Dashboard.UI.Parts;

/// <summary>
/// The sentence each tab's header leads with: a fixed template filled from the insight
/// that tab reads, so the header never says something the tab under it does not show.
/// </summary>
/// <remarks>
/// Templates and never a model, on <see cref="WeeklyBriefSentences"/>' reasoning. Each
/// is null where its insight has nothing to fill it with — not read yet, unavailable, or
/// a roadmap switched off — and the header then names the tab instead.
/// </remarks>
public static class DashboardHeadlines
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>"Week 42 · 4 repositories, last 8 weeks": the ISO week of today, what the
    /// repository chips hold — how many repositories with none taken, the chips' label
    /// otherwise — and the window.</summary>
    public static string Overview(DateOnly today, DashboardScope scope, int repositoryCount)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var week = ISOWeek.GetWeekOfYear(today.ToDateTime(TimeOnly.MinValue));
        var repositories = scope.IsAllRepositories
            ? Count(repositoryCount, "repository", "repositories")
            : scope.Repositories.Label;

        return $"Week {week} · {repositories}, last {scope.Weeks} weeks";
    }

    /// <summary>"143 pull requests merged, 17% came back" — the share that came back is
    /// of the reviewed ones, the Productivity tab's rework rate. "or more" when the window
    /// could not be read whole.</summary>
    public static string? Productivity(ProductivityHeadline? headline)
    {
        if (headline is null) return null;

        var merged = headline.PullRequestsMerged == 1 && headline.Complete
            ? "1 pull request merged"
            : $"{headline.PullRequestsMerged.ToString("N0", Culture)}{(headline.Complete ? string.Empty : " or more")} pull requests merged";

        // A share of nothing reviewed is not a figure, so the clause says so instead.
        return headline.PullRequestsReviewed == 0
            ? $"{merged}, none reviewed"
            : $"{merged}, {MetricFormat.Percent(headline.ReworkRate, 0)} came back";
    }

    /// <summary>"41 of 55 points done, 1 behind pace": the roadmap's done effort of its
    /// planned effort, and how many items are late — behind or overdue.</summary>
    public static string? Tasks(PlanInsight? plan)
    {
        if (plan is not { RoadmapEnabled: true }) return null;

        var points = plan.PlannedEffort == 1 ? "point" : "points";
        var behind = plan.ItemsBehind == 0 ? "none" : plan.ItemsBehind.ToString(Culture);

        return $"{plan.DoneEffort.ToString(Culture)} of {plan.PlannedEffort.ToString(Culture)} {points} done, {behind} behind pace";
    }

    /// <summary>"64 sessions, 38 h of agent time": whole hours, or minutes under one.</summary>
    public static string? Sessions(AssistantSessionsInsight? sessions)
    {
        if (sessions is null) return null;

        var time = sessions.ActiveTime.TotalHours >= 1
            ? $"{Math.Round(sessions.ActiveTime.TotalHours, MidpointRounding.AwayFromZero).ToString("N0", Culture)} h"
            : $"{(int)sessions.ActiveTime.TotalMinutes} m";

        return $"{Count(sessions.Sessions, "session", "sessions")}, {time} of agent time";
    }

    /// <summary>"34 of 42 units agree with the code": the units whose last verdict is
    /// aligned. With no unit verified, the drift issues still open.</summary>
    public static string? Devbook(DriftInsight? drift)
    {
        if (drift is null) return null;

        if (drift.UnitCount == 0)
        {
            return $"No unit verified yet, {Count(drift.Issues.Count, "drift issue", "drift issues")} open";
        }

        var aligned = drift.Directions
            .SelectMany(direction => direction.Units)
            .Count(unit => string.Equals(unit.Verdict?.Trim(), "aligned", StringComparison.OrdinalIgnoreCase));

        return drift.UnitCount == 1
            ? $"{aligned.ToString(Culture)} of 1 unit agrees with the code"
            : $"{aligned.ToString(Culture)} of {drift.UnitCount.ToString(Culture)} units agree with the code";
    }

    /// <summary>
    /// "€212 this month, heading for €312" for one provider; one clause per provider
    /// otherwise, each named and in its own currency — "Anthropic €178 this month, heading
    /// for €266; Azure AI Foundry €34, heading for €46". Never summed: an estimate and a
    /// charge, or two currencies, do not add up.
    /// </summary>
    public static string? Cost(SpendProjectionInsight? projection)
    {
        if (projection is null || projection.Providers.Count == 0) return null;

        if (projection.Providers is [var only])
        {
            return $"{Money(only.MonthToDate)} this month, heading for {Money(only.Projected)}";
        }

        return string.Join("; ", projection.Providers.Select((provider, index) => index == 0
            ? $"{provider.Provider.DisplayName()} {Money(provider.MonthToDate)} this month, heading for {Money(provider.Projected)}"
            : $"{provider.Provider.DisplayName()} {Money(provider.MonthToDate)}, heading for {Money(provider.Projected)}"));
    }

    /// <summary>
    /// An amount as a headline writes one: the currency's sign before it where it has a
    /// common one (€, $, £), its code after it otherwise; whole from a hundred up or when
    /// there are no cents, to the cent below that.
    /// </summary>
    public static string Money(DashboardMoney money)
    {
        ArgumentNullException.ThrowIfNull(money);

        return money.Format();
    }

    private static string Count(int count, string singular, string plural) =>
        count == 1 ? $"1 {singular}" : $"{count.ToString("N0", Culture)} {plural}";
}
