using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI.Parts;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The weekly brief's sentence templates: a figure and its comparison with the week
/// before, which goes up, down or stays put, and a count of one that is singular.
/// </summary>
public sealed class WeeklyBriefSentencesTests
{
    [Fact]
    public void A_count_that_went_up_says_how_many_more()
    {
        Assert.Equal(
            "23 pull requests merged — 4 more than last week.",
            WeeklyBriefSentences.Merged(23, 19));
    }

    [Fact]
    public void A_count_that_went_down_says_how_many_fewer()
    {
        Assert.Equal(
            "15 pull requests merged — 4 fewer than last week.",
            WeeklyBriefSentences.Merged(15, 19));
    }

    [Fact]
    public void A_count_that_did_not_move_says_as_many()
    {
        Assert.Equal(
            "19 pull requests merged — as many as last week.",
            WeeklyBriefSentences.Merged(19, 19));
    }

    [Fact]
    public void A_count_of_one_is_singular()
    {
        Assert.Equal(
            "1 pull request merged — 1 fewer than last week.",
            WeeklyBriefSentences.Merged(1, 2));
        Assert.Equal(
            "1 story point done — 1 more than last week.",
            WeeklyBriefSentences.PointsDone(1m, 0m));
        Assert.Equal("1 issue closed, and no merge that week was reviewed.", WeeklyBriefSentences.ShippingDetail(1, null));
        Assert.Equal("1 session was last active that week.", WeeklyBriefSentences.SessionsDetail(1));
    }

    [Fact]
    public void Zero_is_plural_and_a_figure()
    {
        Assert.Equal(
            "0 pull requests merged — 3 fewer than last week.",
            WeeklyBriefSentences.Merged(0, 3));
    }

    /// <summary>No earlier week is no comparison, never one against a zero that would
    /// read as a quiet week.</summary>
    [Fact]
    public void Without_an_earlier_week_the_comparison_is_left_off()
    {
        Assert.Equal("8 story points done.", WeeklyBriefSentences.PointsDone(8m, null));
    }

    [Fact]
    public void Hours_and_money_compare_as_more_less_or_as_much()
    {
        Assert.Equal("38.0h of agent time — 5.0h more than last week.", WeeklyBriefSentences.AgentTime(38m, 33m));
        Assert.Equal("30.5h of agent time — 2.5h less than last week.", WeeklyBriefSentences.AgentTime(30.5m, 33m));
        Assert.Equal("33.0h of agent time — as much as last week.", WeeklyBriefSentences.AgentTime(33m, 33m));

        Assert.Equal(
            "$212 spent — $28 more than last week.",
            WeeklyBriefSentences.Spent(new DashboardMoney(212m, "USD"), new DashboardMoney(184m, "USD")));
        Assert.Equal(
            "$150 spent — $34 less than last week.",
            WeeklyBriefSentences.Spent(new DashboardMoney(150m, "USD"), new DashboardMoney(184m, "USD")));
        Assert.Equal(
            "$184 spent — as much as last week.",
            WeeklyBriefSentences.Spent(new DashboardMoney(184m, "USD"), new DashboardMoney(184m, "USD")));
    }

    /// <summary>Two currencies cannot be compared without a rate, so the comparison is
    /// left off rather than subtracting one from the other.</summary>
    [Fact]
    public void Money_in_another_currency_is_not_compared()
    {
        Assert.Equal(
            "€12 spent.",
            WeeklyBriefSentences.Spent(new DashboardMoney(12m, "EUR"), new DashboardMoney(9m, "USD")));
    }

    [Fact]
    public void The_shipping_detail_says_the_review_wait_or_that_nothing_was_reviewed()
    {
        Assert.Equal(
            "4 issues closed, and a first review took a median of 3.2h.",
            WeeklyBriefSentences.ShippingDetail(4, 3.2m));

        // Entry 19, AC35: a week with no reviewed merge says so rather than dropping
        // the clause.
        Assert.Equal("4 issues closed, and no merge that week was reviewed.", WeeklyBriefSentences.ShippingDetail(4, null));

        // A review that arrived within the hour is still a review.
        Assert.Equal("4 issues closed, and a first review took a median of 0h.", WeeklyBriefSentences.ShippingDetail(4, 0m));
    }

    /// <summary>The brief's narrative title (decision Q5, AC33): the best or quietest
    /// shipping week of the quarter read, else the week's counts; ", at a cost" when a
    /// provider is projected past its budget.</summary>
    [Fact]
    public void The_title_names_the_best_shipping_week_of_the_quarter()
    {
        Assert.Equal(
            "Your best shipping week this quarter.",
            WeeklyBriefSentences.Title(23, [12, 18, 23, 9], 14m, atACost: false));
        Assert.Equal(
            "Your best shipping week this quarter, at a cost.",
            WeeklyBriefSentences.Title(23, [12, 18, 23, 9], 14m, atACost: true));
    }

    [Fact]
    public void The_title_names_the_quietest_shipping_week_of_the_quarter()
    {
        Assert.Equal(
            "Your quietest shipping week this quarter.",
            WeeklyBriefSentences.Title(9, [12, 18, 23, 9], 14m, atACost: false));
    }

    [Fact]
    public void Otherwise_the_title_is_the_weeks_counts()
    {
        Assert.Equal(
            "18 pull requests merged, 14 story points done.",
            WeeklyBriefSentences.Title(18, [12, 18, 23, 9], 14m, atACost: false));
        Assert.Equal(
            "1 pull request merged, 1 story point done, at a cost.",
            WeeklyBriefSentences.Title(1, [1, 1, 1], 1m, atACost: true));
        Assert.Equal(
            "0 pull requests merged.",
            WeeklyBriefSentences.Title(0, [0, 0], null, atACost: false));
    }

    /// <summary>The Plan figure is the running total against the plan (AC34).</summary>
    [Fact]
    public void The_plan_figure_is_done_of_planned()
    {
        Assert.Equal("41 of 55", WeeklyBriefSentences.PlanFigure(41, 55));
        Assert.Equal("0 of 1", WeeklyBriefSentences.PlanFigure(0, 1));
    }

    /// <summary>The Cost figure is month-to-date against the budget, one per provider in
    /// its own currency (AC34, decision Q3).</summary>
    [Fact]
    public void The_cost_figure_is_month_to_date_against_each_budget()
    {
        var projection = new SpendProjectionInsight(
        [
            Projection(SpendProvider.Claude, 178m, 250m, "EUR"),
            Projection(SpendProvider.AzureFoundry, 34m, 50m, "EUR"),
            Projection(SpendProvider.Copilot, 9m, null, "USD")
        ]);

        Assert.Equal("€178 of €250 · €34 of €50 · $9", WeeklyBriefSentences.CostFigure(projection));
        Assert.Equal("€212 of €300", WeeklyBriefSentences.CostFigure(new SpendProjectionInsight([Projection(SpendProvider.Claude, 212m, 300m, "EUR")])));
        Assert.Null(WeeklyBriefSentences.CostFigure(SpendProjectionInsight.Empty));
    }

    [Fact]
    public void The_cost_detail_names_a_provider_as_the_screen_does()
    {
        var projection = new SpendProjectionInsight([Projection(SpendProvider.AzureFoundry, 60m, 50m, "EUR", projected: 90m)]);

        Assert.Equal("Azure AI Foundry is on course to pass its budget this month.", WeeklyBriefSentences.CostDetail(projection));
    }

    private static SpendProjection Projection(SpendProvider provider, decimal spent, decimal? budget, string currency, decimal? projected = null) =>
        new(
            provider,
            new DashboardMoney(spent, currency),
            new DashboardMoney(1m, currency),
            new DashboardMoney(projected ?? spent, currency),
            budget is { } amount ? new DashboardMoney(amount, currency) : null,
            new DateOnly(2026, 10, 12),
            19,
            IsEstimate: false);

    [Fact]
    public void The_plan_detail_counts_the_items_behind_pace()
    {
        var item = new PlanItemProgress(
            Guid.NewGuid(), "Multi-account support",
            new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 31), ["backlog"],
            GatheredCount: 6, DoneCount: 4, TotalEffort: 21, DoneEffort: 15, Unestimated: 0,
            IsFinished: false, LastCompletedOn: null, Parts: [], PlacedByEffort: false);

        Assert.Equal(
            "1 roadmap item is behind pace, with 15 points of 21 done.",
            WeeklyBriefSentences.PlanDetail(new PlanInsight(true, new PlanPace(5m, PlanPaceBasis.Manual),
                [new PlanItemInsight(item, PlanOutlook.Behind, null)])));
        Assert.Equal(
            "Every roadmap item in view is on pace, with 15 points of 21 done.",
            WeeklyBriefSentences.PlanDetail(new PlanInsight(true, new PlanPace(5m, PlanPaceBasis.Manual),
                [new PlanItemInsight(item, PlanOutlook.OnTrack, null)])));
        // Past its end with work open is behind pace too, the one definition the Overview
        // tile, Needs you and the Tasks tab share.
        Assert.Equal(
            "2 roadmap items are behind pace, with 30 points of 42 done.",
            WeeklyBriefSentences.PlanDetail(new PlanInsight(true, new PlanPace(5m, PlanPaceBasis.Manual),
                [new PlanItemInsight(item, PlanOutlook.Behind, null), new PlanItemInsight(item, PlanOutlook.Overdue, null)])));
        Assert.Equal("The roadmap is switched off.", WeeklyBriefSentences.PlanDetail(PlanInsight.Off));
        Assert.Equal("The roadmap could not be read.", WeeklyBriefSentences.PlanDetail(null));
    }

    [Fact]
    public void A_missing_figure_is_one_line_carrying_the_reason()
    {
        Assert.Equal(
            "No cost figures for this week: No provider is configured.",
            WeeklyBriefSentences.Missing("Cost", "No provider is configured."));
    }

    /// <summary>The last completed week is the Monday-to-Sunday one before the week today
    /// falls in, and the reading takes that point off a series whose last point is
    /// today's week.</summary>
    [Fact]
    public void The_reported_week_is_the_last_completed_one_and_reads_back_eight_bars()
    {
        // A Wednesday in ISO week 42 of 2026.
        var week = BriefWeek.Of(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero), 0);

        Assert.Equal(new DateOnly(2026, 10, 5), week.Monday);
        Assert.Equal(new DateOnly(2026, 10, 11), week.Sunday);
        Assert.Equal(41, week.Number);
        Assert.Equal(42, week.NextNumber);
        Assert.Equal("Week 41 · 05 Oct – 11 Oct", WeeklyBriefSentences.WeekLabel(week));

        var series = Enumerable.Range(30, 13).Select(n => new InsightPoint($"W{n}", n)).ToList();
        var reading = week.Read(series)!;

        Assert.Equal(41m, reading.Value);
        Assert.Equal(40m, reading.Previous);
        Assert.Equal(8, reading.Bars.Count);
        Assert.Equal("W41", reading.Bars[^1].Label);

        var earliest = BriefWeek.Of(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero), 99);
        Assert.Equal(BriefWeek.MaxWeeksBack, earliest.WeeksBack);
        Assert.False(earliest.HasEarlier);
        Assert.Equal(8, earliest.Read(series)!.Bars.Count);
        Assert.Null(week.Read([new InsightPoint("W42", 1m)]));
    }
}
