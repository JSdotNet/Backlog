using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI.Parts;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The sentence each tab's header leads with (AC4): a fixed template filled from the
/// insight the tab itself reads, null where that insight has nothing to fill it with.
/// </summary>
public sealed class DashboardHeadlinesTests
{
    private static readonly DateOnly Monday12October = new(2026, 10, 12);

    [Fact]
    public void The_overview_names_the_week_the_repositories_and_the_window()
    {
        var headline = DashboardHeadlines.Overview(Monday12October, new DashboardScope(Period: DashboardPeriod.EightWeeks), repositoryCount: 4);

        Assert.Equal("Week 42 · 4 repositories, last 8 weeks", headline);
    }

    [Fact]
    public void The_overview_names_one_repository_in_the_singular()
    {
        var headline = DashboardHeadlines.Overview(Monday12October, DashboardScope.Default, repositoryCount: 1);

        Assert.Equal("Week 42 · 1 repository, last 12 weeks", headline);
    }

    [Fact]
    public void A_focused_overview_names_the_repositories_the_chips_hold()
    {
        var scope = new DashboardScope(RepositoryFocus.Of("backlog", "marketplace"), DashboardPeriod.FourWeeks);

        Assert.Equal("Week 42 · backlog, marketplace, last 4 weeks", DashboardHeadlines.Overview(Monday12October, scope, repositoryCount: 4));
    }

    [Fact]
    public void Productivity_says_how_many_merged_and_what_share_came_back()
    {
        var headline = new ProductivityHeadline(143, 0, 0.17m, null, [], [], []);

        Assert.Equal("143 pull requests merged, 17% came back", DashboardHeadlines.Productivity(headline));
    }

    [Fact]
    public void Productivity_is_singular_for_one_and_a_floor_when_the_window_was_cut_short()
    {
        Assert.Equal("1 pull request merged, 0% came back", DashboardHeadlines.Productivity(new ProductivityHeadline(1, 0, 0m, null, [], [], [])));
        Assert.Equal(
            "143 or more pull requests merged, 17% came back",
            DashboardHeadlines.Productivity(new ProductivityHeadline(143, 0, 0.17m, null, [], [], []) { Complete = false }));
    }

    /// <summary>With no reviewed merge there is no share to give: the clause says none
    /// were reviewed rather than "0% came back", as the Overview prints "—".</summary>
    [Fact]
    public void Productivity_with_no_reviewed_merge_says_none_were_reviewed()
    {
        Assert.Equal(
            "881 or more pull requests merged, none reviewed",
            DashboardHeadlines.Productivity(new ProductivityHeadline(881, 0, 0m, null, [], [], []) { Complete = false, PullRequestsReviewed = 0 }));
    }

    [Fact]
    public void Tasks_say_the_points_done_of_planned_and_how_many_items_are_behind()
    {
        var plan = Plan((30, 20, PlanOutlook.Behind), (25, 21, PlanOutlook.OnTrack));

        Assert.Equal("41 of 55 points done, 1 behind pace", DashboardHeadlines.Tasks(plan));
    }

    [Fact]
    public void Tasks_with_nothing_behind_say_so_and_one_point_is_singular()
    {
        Assert.Equal("0 of 1 point done, none behind pace", DashboardHeadlines.Tasks(Plan((1, 0, PlanOutlook.OnTrack))));
    }

    [Fact]
    public void Tasks_have_no_sentence_with_the_roadmap_off()
    {
        Assert.Null(DashboardHeadlines.Tasks(PlanInsight.Off));
    }

    [Fact]
    public void Sessions_say_how_many_and_the_agent_time()
    {
        var insight = new AssistantSessionsInsight(64, TimeSpan.FromHours(38.2), null, 0, false, [], []);

        Assert.Equal("64 sessions, 38 h of agent time", DashboardHeadlines.Sessions(insight));
        Assert.Equal("1 session, 40 m of agent time", DashboardHeadlines.Sessions(insight with { Sessions = 1, ActiveTime = TimeSpan.FromMinutes(40) }));

        // Grouped as the tile under it groups it (verify: "1937" over "1,937").
        Assert.Equal("1,937 sessions, 1,485 h of agent time", DashboardHeadlines.Sessions(insight with { Sessions = 1937, ActiveTime = TimeSpan.FromHours(1485) }));
    }

    [Fact]
    public void Devbook_says_how_many_units_agree_with_the_code()
    {
        var drift = new DriftInsight(
            [new DriftDirection("push", [Unit("aligned"), Unit("aligned"), Unit("code-ahead")])],
            [],
            null);

        Assert.Equal("2 of 3 units agree with the code", DashboardHeadlines.Devbook(drift));
        Assert.Equal("1 of 1 unit agrees with the code", DashboardHeadlines.Devbook(new DriftInsight([new DriftDirection("push", [Unit("Aligned")])], [], null)));
    }

    [Fact]
    public void Devbook_with_no_verified_unit_counts_the_open_issues()
    {
        var drift = new DriftInsight([], [new DriftIssue("backlog", 1, "[Devbook drift] x", "u", false)], null);

        Assert.Equal("No unit verified yet, 1 drift issue open", DashboardHeadlines.Devbook(drift));
    }

    [Fact]
    public void Cost_for_one_provider_is_spent_and_heading_for()
    {
        var projection = new SpendProjectionInsight([Projection(SpendProvider.AzureFoundry, 212m, 312m, "EUR")]);

        Assert.Equal("€212 this month, heading for €312", DashboardHeadlines.Cost(projection));
    }

    /// <summary>Several providers stay apart, one clause each, in their own currencies
    /// (decision Q3): an estimate and a charge, or two currencies, never add up.</summary>
    [Fact]
    public void Cost_for_several_providers_is_one_clause_each()
    {
        var projection = new SpendProjectionInsight(
        [
            Projection(SpendProvider.Claude, 178m, 266m, "EUR"),
            Projection(SpendProvider.AzureFoundry, 34m, 46m, "EUR")
        ]);

        Assert.Equal(
            "Anthropic €178 this month, heading for €266; Azure AI Foundry €34, heading for €46",
            DashboardHeadlines.Cost(projection));
    }

    [Fact]
    public void Cost_keeps_each_providers_own_currency()
    {
        var projection = new SpendProjectionInsight(
        [
            Projection(SpendProvider.Claude, 45.5m, 80m, "USD"),
            Projection(SpendProvider.AzureFoundry, 7.25m, 9m, "CHF")
        ]);

        Assert.Equal(
            "Anthropic $45.50 this month, heading for $80; Azure AI Foundry 7.25 CHF, heading for 9 CHF",
            DashboardHeadlines.Cost(projection));
    }

    [Fact]
    public void A_missing_source_has_no_sentence()
    {
        Assert.Null(DashboardHeadlines.Productivity(null));
        Assert.Null(DashboardHeadlines.Tasks(null));
        Assert.Null(DashboardHeadlines.Sessions(null));
        Assert.Null(DashboardHeadlines.Devbook(null));
        Assert.Null(DashboardHeadlines.Cost(null));
        Assert.Null(DashboardHeadlines.Cost(SpendProjectionInsight.Empty));
    }

    private static PlanInsight Plan(params (int Total, int Done, PlanOutlook Outlook)[] items) =>
        new(
            true,
            new PlanPace(5m, PlanPaceBasis.LastFourWeeks),
            [.. items.Select((item, index) => new PlanItemInsight(
                new PlanItemProgress(
                    Guid.NewGuid(),
                    $"Item {index}",
                    new DateOnly(2026, 9, 1),
                    new DateOnly(2026, 10, 30),
                    [],
                    GatheredCount: 1,
                    DoneCount: 0,
                    TotalEffort: item.Total,
                    DoneEffort: item.Done,
                    Unestimated: 0,
                    IsFinished: false,
                    LastCompletedOn: null,
                    Parts: [],
                    PlacedByEffort: false),
                item.Outlook,
                null))]);

    private static DriftUnit Unit(string verdict) =>
        new("backlog", ".devbook/domain/x.md#" + Guid.NewGuid().ToString("N"), "aggregate", "push", null, verdict, null, null, DateTimeOffset.UnixEpoch);

    private static SpendProjection Projection(SpendProvider provider, decimal spent, decimal projected, string currency) =>
        new(
            provider,
            new DashboardMoney(spent, currency),
            new DashboardMoney(1m, currency),
            new DashboardMoney(projected, currency),
            null,
            new DateOnly(2026, 10, 12),
            19,
            IsEstimate: false);
}
