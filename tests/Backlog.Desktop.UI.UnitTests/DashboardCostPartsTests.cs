using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI.Parts;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Cost tab's three parts: a card per provider with its budget, spend over the month
/// with the projected days faded, and spend by model with tokens by direction.
/// </summary>
public sealed class DashboardCostPartsTests
{
    private static readonly DateOnly MonthStart = new(2026, 10, 1);

    private static readonly DateOnly Today = new(2026, 10, 7);

    private static readonly MonthlySpend Claude = new(
        SpendProvider.Claude, new DashboardMoney(178m, "USD"), Allowance: null, MonthStart, Today, IsEstimate: true);

    private static readonly MonthlySpend Copilot = new(
        SpendProvider.Copilot, new DashboardMoney(34m, "USD"), new DashboardMoney(12m, "USD"), MonthStart, Today, IsEstimate: false);

    private static readonly MonthlySpend Azure = new(
        SpendProvider.AzureFoundry, new DashboardMoney(9m, "USD"), Allowance: null, MonthStart, Today, IsEstimate: false);

    [Fact]
    public void A_card_shows_spend_of_budget_with_a_projected_marker_and_says_it_will_pass()
    {
        using var context = Context(new ScriptedCosts
        {
            Month = [Claude],
            Projections = [Projection(SpendProvider.Claude, spent: 178m, projected: 266m, budget: 250m)]
        });

        var part = context.Render<SpendThisMonthPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        var card = part.Find("[data-testid='dashboard-spend-month-claude']");
        Assert.Contains("of 250.00 USD", card.QuerySelector(".metric-tile__unit")!.TextContent, StringComparison.Ordinal);

        var meter = part.Find("[data-testid='dashboard-spend-month-meter-claude']");
        Assert.Equal("178", meter.QuerySelector("[role=meter]")!.GetAttribute("aria-valuenow"));
        Assert.Equal("250", meter.QuerySelector("[role=meter]")!.GetAttribute("aria-valuemax"));
        Assert.Equal("left: 100%", meter.QuerySelector(".metric-meter__marker")!.GetAttribute("style"));

        var chip = part.Find("[data-testid='dashboard-spend-month-chip-claude']");
        Assert.Equal("Will pass budget", chip.TextContent.Trim());
        Assert.Contains("dashboard-spend-card__chip--over", chip.ClassList);

        Assert.Contains("projected 266.00 USD", card.QuerySelector(".metric-tile__footnote")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_card_inside_its_budget_says_so()
    {
        using var context = Context(new ScriptedCosts
        {
            Month = [Azure],
            Projections = [Projection(SpendProvider.AzureFoundry, spent: 9m, projected: 46m, budget: 50m)]
        });

        var part = context.Render<SpendThisMonthPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        var chip = part.Find("[data-testid='dashboard-spend-month-chip-azure-foundry']");
        Assert.Equal("Within budget", chip.TextContent.Trim());
        Assert.Equal("left: 92%", part.Find(".metric-meter__marker").GetAttribute("style"));
    }

    /// <summary>A meter needs a ceiling. With no budget set the card shows the spend and
    /// where it is heading, and nothing to fill.</summary>
    [Fact]
    public void With_no_budget_a_card_shows_spend_and_projection_without_a_meter()
    {
        using var context = Context(new ScriptedCosts
        {
            Month = [Claude],
            Projections = [Projection(SpendProvider.Claude, spent: 178m, projected: 266m, budget: null)]
        });

        var part = context.Render<SpendThisMonthPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        var card = part.Find("[data-testid='dashboard-spend-month-claude']");
        Assert.Contains("178.00 USD", card.TextContent, StringComparison.Ordinal);
        Assert.Contains("projected 266.00 USD", card.TextContent, StringComparison.Ordinal);
        Assert.Empty(part.FindAll("[role=meter]"));
        Assert.Empty(part.FindAll(".dashboard-spend-card__chip"));
        Assert.Null(card.QuerySelector(".metric-tile__unit"));
    }

    [Fact]
    public void A_provider_that_reports_an_allowance_says_what_was_charged_against_what_was_consumed()
    {
        using var context = Context(new ScriptedCosts
        {
            Month = [Copilot],
            Projections = [Projection(SpendProvider.Copilot, spent: 34m, projected: 40m, budget: 50m)]
        });

        var part = context.Render<SpendThisMonthPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        Assert.Equal(
            "Charged 34.00 USD against 46.00 USD consumed · projected 40.00 USD",
            part.Find("[data-testid='dashboard-spend-month-copilot'] .metric-tile__footnote").TextContent.Trim());
    }

    /// <summary>The spend still stands when the projection cannot be read: the card loses
    /// its meter and chip, not its figure.</summary>
    [Fact]
    public void Without_a_projection_the_card_keeps_its_spend()
    {
        using var context = Context(new ScriptedCosts { Month = [Claude], ProjectionRefused = true });

        var part = context.Render<SpendThisMonthPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        Assert.Contains("178.00 USD", part.Find("[data-testid='dashboard-spend-month-claude']").TextContent, StringComparison.Ordinal);
        Assert.Empty(part.FindAll("[role=meter]"));
    }

    [Fact]
    public void Spend_over_time_opens_cumulative_with_the_budget_line_and_the_projected_days_faded()
    {
        using var context = Context(new ScriptedCosts { ByDay = Month(budget: 300m) });

        var part = context.Render<SpendTrendPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        Assert.Equal("true", part.Find("[data-testid='dashboard-spend-trend-cumulative']").GetAttribute("aria-pressed"));

        var columns = part.FindAll("[data-testid='dashboard-spend-trend-bars'] .metric-bars__column");
        Assert.Equal(31, columns.Count);
        Assert.DoesNotContain("metric-bars__column--projected", columns[6].ClassList);
        Assert.Contains("metric-bars__column--projected", columns[7].ClassList);

        // Cumulative: seven days of 10, then 5 a day projected — 70 by today, 190 by the
        // thirty-first.
        var cells = part.FindAll("[data-testid='dashboard-spend-trend-bars'] tbody td");
        Assert.Equal("70.00 USD", cells[6].TextContent.Trim());
        Assert.Equal("190.00 USD (projected)", cells[30].TextContent.Trim());

        Assert.Equal("Budget 300.00 USD", part.Find(".metric-bars__reference-label").TextContent);
    }

    [Fact]
    public void Per_day_draws_each_day_and_no_budget_line()
    {
        using var context = Context(new ScriptedCosts { ByDay = Month(budget: 300m) });

        var part = context.Render<SpendTrendPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        part.Find("[data-testid='dashboard-spend-trend-daily']").Click();

        Assert.Equal("true", part.Find("[data-testid='dashboard-spend-trend-daily']").GetAttribute("aria-pressed"));
        var cells = part.FindAll("[data-testid='dashboard-spend-trend-bars'] tbody td");
        Assert.Equal("10.00 USD", cells[6].TextContent.Trim());
        Assert.Equal("5.00 USD (projected)", cells[30].TextContent.Trim());
        Assert.Empty(part.FindAll(".metric-bars__reference"));
    }

    [Fact]
    public void With_no_budget_set_cumulative_draws_no_line()
    {
        using var context = Context(new ScriptedCosts { ByDay = Month(budget: null) });

        var part = context.Render<SpendTrendPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        Assert.Empty(part.FindAll(".metric-bars__reference"));
        Assert.Equal(31, part.FindAll(".metric-bars__column").Count);
    }

    [Fact]
    public void Spend_by_model_shows_share_input_and_output_tokens_and_spend_with_a_dash_where_none_were_reported()
    {
        using var context = Context(new ScriptedCosts
        {
            ByModel =
            [
                new InsightRow("opus", 44_300_000, new DashboardMoney(131m, "USD"), "Claude") { InputTokens = 41_200_000, OutputTokens = 3_100_000 },
                new InsightRow("gpt-5", null, new DashboardMoney(12m, "USD"), "Copilot")
            ]
        });

        var part = context.Render<SpendByModelPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        var table = part.Find("[data-testid='dashboard-spend-model-table']");
        Assert.Equal(
            ["Model", "Share", "Input tokens", "Output tokens", "Spend"],
            table.QuerySelectorAll("thead th").Select(cell => cell.TextContent.Trim()));

        var opus = table.QuerySelectorAll("tbody tr")[0].QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToList();
        Assert.Equal("41.2M", opus[1]);
        Assert.Equal("3.1M", opus[2]);

        var gpt = table.QuerySelectorAll("tbody tr")[1].QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToList();
        Assert.Equal("—", gpt[1]);
        Assert.Equal("—", gpt[2]);
        Assert.Equal("12.00 USD", gpt[3]);
    }

    private static BunitContext Context(ScriptedCosts costs)
    {
        var context = new BunitContext();
        _ = context.Services.AddUnavailableDashboard("backlog");
        context.Services.AddSingleton<ICostInsights>(costs);
        return context;
    }

    private static SpendProjection Projection(SpendProvider provider, decimal spent, decimal projected, decimal? budget) => new(
        provider,
        new DashboardMoney(spent, "USD"),
        new DashboardMoney(1m, "USD"),
        new DashboardMoney(projected, "USD"),
        budget is { } amount ? new DashboardMoney(amount, "USD") : null,
        Today,
        DaysLeft: 24,
        IsEstimate: false);

    /// <summary>October: ten a day through the seventh, five a day projected after it.</summary>
    private static SpendByDayInsight Month(decimal? budget) => new(
        [.. Enumerable.Range(0, 31).Select(offset =>
        {
            var day = MonthStart.AddDays(offset);
            return day <= Today ? new SpendDay(day, 10m, IsProjected: false) : new SpendDay(day, 5m, IsProjected: true);
        })],
        "USD",
        budget is { } amount ? new DashboardMoney(amount, "USD") : null,
        IsEstimate: false);

    private sealed class ScriptedCosts : ICostInsights
    {
        public IReadOnlyList<MonthlySpend> Month { get; init; } = [];

        public IReadOnlyList<SpendProjection> Projections { get; init; } = [];

        public bool ProjectionRefused { get; init; }

        public SpendByDayInsight ByDay { get; init; } = SpendByDayInsight.Empty;

        public IReadOnlyList<InsightRow> ByModel { get; init; } = [];

        public Task<InsightResult<SpendThisMonthInsight>> GetThisMonthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendThisMonthInsight>.Ready(new SpendThisMonthInsight(Month)));

        public Task<InsightResult<SpendTrendInsight>> GetTrendAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendTrendInsight>.Unavailable("Not used by the Cost tab."));

        public Task<InsightResult<SpendByModelInsight>> GetByModelAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByModelInsight>.Ready(new SpendByModelInsight(ByModel)));

        public Task<InsightResult<SpendProjectionInsight>> GetProjectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ProjectionRefused
                ? InsightResult<SpendProjectionInsight>.Unavailable("No projection in this test.")
                : InsightResult<SpendProjectionInsight>.Ready(new SpendProjectionInsight(Projections)));

        public Task<InsightResult<SpendByDayInsight>> GetByDayAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByDayInsight>.Ready(ByDay));

        public void Invalidate()
        {
        }
    }
}
