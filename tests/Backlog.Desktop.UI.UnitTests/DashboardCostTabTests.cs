using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI.Parts;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Cost tab against board Cost (S9; AC27–AC32): the providers' display names, the
/// chip in the card's corner, the axis naming the month's first day, today and its last,
/// the Copilot caption's date range, one token shape, and the mode as one choice.
/// </summary>
public sealed class DashboardCostTabTests
{
    private static readonly DateOnly MonthStart = new(2026, 10, 1);
    private static readonly DateOnly Today = new(2026, 10, 7);

    [Fact]
    public void The_cards_name_the_providers_as_the_board_does()
    {
        using var context = Context(new Costs());

        var part = context.Render<SpendThisMonthPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        Assert.Contains("Anthropic", part.Find("[data-testid='dashboard-spend-month-claude']").TextContent, StringComparison.Ordinal);
        Assert.Contains("Azure AI Foundry", part.Find("[data-testid='dashboard-spend-month-azure-foundry']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>The chip sits in the card's corner beside the provider's name: the card
    /// is its positioning context and the chip its own element (AC28).</summary>
    [Fact]
    public void The_budget_chip_is_in_the_cards_corner()
    {
        using var context = Context(new Costs());

        var part = context.Render<SpendThisMonthPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        var chip = part.Find("[data-testid='dashboard-spend-month-chip-claude']");
        Assert.Contains("dashboard-spend-card__chip", chip.ClassList);
        Assert.Contains("dashboard-spend-card", chip.Closest(".metric-tile")!.ClassList);
    }

    [Fact]
    public void The_copilot_caption_names_its_date_range()
    {
        using var context = Context(new Costs());

        var part = context.Render<SpendThisMonthPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        Assert.Contains("consumed, 1–7 Oct", part.Find("[data-testid='dashboard-spend-month-copilot']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_axis_names_the_first_day_today_and_the_last()
    {
        using var context = Context(new Costs());

        var part = context.Render<SpendTrendPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        var named = part.FindAll(".metric-bars__column-label").Select(label => label.TextContent).Where(text => text.Length > 0);
        Assert.Equal(["1 Oct", "Today, 7 Oct", "31 Oct"], named);
        Assert.Equal("radiogroup", part.Find("[data-testid='dashboard-spend-trend-mode']").GetAttribute("role"));
    }

    /// <summary>One token shape (AC32): input and output columns only; a provider that
    /// reports only a total reads as not reported in both, with no total column.</summary>
    [Fact]
    public void The_model_table_settles_on_input_and_output()
    {
        using var context = Context(new Costs());

        var part = context.Render<SpendByModelPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        var headings = part.FindAll("thead th").Select(cell => cell.TextContent.Trim()).ToList();
        Assert.DoesNotContain("Tokens", headings);
        Assert.Contains(headings, heading => heading.Contains("Input", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(headings, heading => heading.Contains("Output", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("—", part.FindAll("tbody tr").Single(row => row.TextContent.Contains("gpt-5", StringComparison.Ordinal)).TextContent, StringComparison.Ordinal);
    }

    private static BunitContext Context(ICostInsights costs)
    {
        var context = new BunitContext();
        _ = context.Services.AddUnavailableDashboard("backlog");
        context.Services.AddSingleton(costs);
        return context;
    }

    private sealed class Costs : ICostInsights
    {
        public Task<InsightResult<SpendThisMonthInsight>> GetThisMonthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendThisMonthInsight>.Ready(new SpendThisMonthInsight(
            [
                new MonthlySpend(SpendProvider.Claude, new DashboardMoney(178m, "USD"), null, MonthStart, Today, true),
                new MonthlySpend(SpendProvider.Copilot, new DashboardMoney(34m, "USD"), new DashboardMoney(12m, "USD"), MonthStart, Today, false),
                new MonthlySpend(SpendProvider.AzureFoundry, new DashboardMoney(20m, "EUR"), null, MonthStart, Today, false)
            ])));

        public Task<InsightResult<SpendByModelInsight>> GetByModelAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByModelInsight>.Ready(new SpendByModelInsight(
            [
                new InsightRow("opus", 1_000, new DashboardMoney(12m, "USD"), "Anthropic") { InputTokens = 400, OutputTokens = 600 },
                new InsightRow("gpt-5", 2_000, new DashboardMoney(9m, "USD"), "Copilot")
            ])));

        public Task<InsightResult<SpendProjectionInsight>> GetProjectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendProjectionInsight>.Ready(new SpendProjectionInsight(
            [
                new SpendProjection(
                    SpendProvider.Claude,
                    new DashboardMoney(178m, "USD"),
                    new DashboardMoney(10m, "USD"),
                    new DashboardMoney(420m, "USD"),
                    new DashboardMoney(300m, "USD"),
                    Today,
                    24,
                    true)
            ])));

        public Task<InsightResult<SpendByDayInsight>> GetByDayAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByDayInsight>.Ready(new SpendByDayInsight(
                [.. Enumerable.Range(0, 31).Select(day => new SpendDay(MonthStart.AddDays(day), 5m, IsProjected: day >= 7))],
                "USD",
                null,
                false)));

        public Task<InsightResult<SpendByWeekInsight>> GetByWeekAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByWeekInsight>.Unavailable("Not used."));

        public void Invalidate()
        {
        }
    }
}
