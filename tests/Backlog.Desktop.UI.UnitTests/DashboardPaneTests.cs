using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The dashboard surface: its fixed composition, its filter, and the independence
/// its seven parts promise.
/// </summary>
public class DashboardPaneTests
{
    /// <summary>
    /// Every part, on a machine where no provider is reachable. This is the state a
    /// fresh install is in, and a pane that only rendered with data behind it would
    /// fail here.
    /// </summary>
    [Fact]
    public void Every_part_renders_even_when_no_provider_can_answer()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();

        foreach (var part in new[]
                 {
                     "dashboard-headline",
                     "dashboard-score",
                     "dashboard-rework",
                     "dashboard-trend",
                     "dashboard-spend-month",
                     "dashboard-spend-trend",
                     "dashboard-spend-model"
                 })
        {
            Assert.NotNull(pane.Find($"[data-testid='{part}']"));
        }
    }

    /// <summary>
    /// The independence claim, asserted rather than described: each part carries its
    /// source's own words, so one unconfigured provider explains itself instead of
    /// blanking the surface.
    /// </summary>
    [Fact]
    public void A_part_whose_source_refuses_carries_that_sources_reason()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();

        Assert.Contains(DashboardTestHost.UnavailableReason, pane.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// One part failing must not take another down. Productivity is refused here and
    /// cost answers, so the cost figures have to be on screen beside the explanation.
    /// </summary>
    [Fact]
    public void One_source_refusing_leaves_the_other_parts_figures_on_screen()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ICostInsights>(new ReadyCostInsights()));

        var pane = context.Render<DashboardPane>();

        // Productivity explains itself...
        Assert.Contains(DashboardTestHost.UnavailableReason, pane.Markup, StringComparison.Ordinal);

        // ...and the cost tile still shows its money.
        var tile = pane.Find("[data-testid='dashboard-spend-month-claude']");
        Assert.Contains("12.34", tile.TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// A source that genuinely yields before answering, which every real provider
    /// does and no synchronous test double does.
    /// </summary>
    /// <remarks>
    /// This is a regression test with a specific bug behind it. The part base used to
    /// await its fetch with <c>ConfigureAwait(false)</c>, which took the continuation
    /// off the renderer's dispatcher; <c>StateHasChanged</c> asserts it is on that
    /// dispatcher, so it threw and took the whole circuit down. Against doubles that
    /// returned an already-completed task the awaits resumed synchronously, the flag
    /// did nothing, and every test passed — the failure only appeared against a real
    /// provider. So the double here yields on purpose.
    /// </remarks>
    [Fact]
    public async Task A_source_that_answers_asynchronously_still_leaves_the_loading_state()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(new YieldingProductivityInsights()));

        var pane = context.Render<DashboardPane>();

        await pane.InvokeAsync(() => { });

        pane.WaitForAssertion(() =>
        {
            var headline = pane.Find("[data-testid='dashboard-headline']");
            Assert.DoesNotContain("Loading", headline.TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Answered after yielding.", headline.TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_filter_offers_every_configured_repository_and_an_all_repositories_option()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        var options = pane.FindAll("[data-testid='dashboard-repository-filter'] option");

        Assert.Equal(3, options.Count);
        Assert.Equal("All repositories", options[0].TextContent);
        Assert.Contains(options, option => option.TextContent == "JSdotNet/backlog");
        Assert.Contains(options, option => option.TextContent == "JSdotNet/backlog-ide");
    }

    /// <summary>
    /// Choosing a repository has to reach the parts, or the filter is a control that
    /// silently drives half a page — which is the usual way a dashboard goes stale.
    /// </summary>
    [Fact]
    public void Focusing_a_repository_reaches_the_productivity_parts()
    {
        var productivity = new RecordingProductivityInsights();

        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(productivity));

        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-repository-filter'] select").Change("backlog-ide");

        Assert.Contains("backlog-ide", productivity.Scopes.Select(scope => scope.RepositoryAlias));
    }

    /// <summary>
    /// The cost parts cannot narrow by repository, because neither provider reports
    /// spend that way. They must not re-fetch when the filter moves, or the dashboard
    /// would spend a call budget to produce the identical answer.
    /// </summary>
    [Fact]
    public void Focusing_a_repository_does_not_re_ask_the_cost_parts()
    {
        var costs = new RecordingCostInsights();

        using var context = Context(configure: services => services.AddSingleton<ICostInsights>(costs));

        var pane = context.Render<DashboardPane>();
        var afterFirstRender = costs.Calls;

        pane.Find("[data-testid='dashboard-repository-filter'] select").Change("backlog-ide");

        Assert.Equal(afterFirstRender, costs.Calls);
    }

    /// <summary>
    /// The constraint has to be on screen, not only in the code. A reader who cannot
    /// see why a figure did not move when they filtered will conclude the filter is
    /// broken.
    /// </summary>
    [Fact]
    public void The_cost_section_says_the_repository_filter_does_not_reach_it()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        var cost = pane.Find("[data-testid='dashboard-cost']");

        Assert.Contains("does not change anything in this section", cost.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_can_be_narrowed_to_four_weeks()
    {
        var productivity = new RecordingProductivityInsights();

        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(productivity));

        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-window-4']").Click();

        Assert.Contains(DashboardPeriod.FourWeeks, productivity.Scopes.Select(scope => scope.Period));
    }

    [Fact]
    public void Refreshing_a_part_asks_its_source_again()
    {
        var productivity = new RecordingProductivityInsights();

        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(productivity));

        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-headline-refresh']").Click();

        Assert.True(productivity.Invalidations > 0);
    }

    /// <summary>
    /// The surface is deliberately not configurable — no layout editing, no adding or
    /// removing a part, nothing persisted. This is the guard against that quietly
    /// changing: the only controls on the panel are the filter, the per-part refresh,
    /// and the close button.
    /// </summary>
    [Fact]
    public void The_panel_offers_no_way_to_configure_itself()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();

        // Phrases rather than single words. A bare "configure" would match the
        // headline's own note about configured repositories, which is the pane saying
        // where its figures come from — the opposite of an affordance.
        var forbidden = new[]
        {
            "customise",
            "customize",
            "add widget",
            "remove widget",
            "edit layout",
            "reset layout",
            "rearrange"
        };

        foreach (var phrase in forbidden)
        {
            Assert.DoesNotContain(phrase, pane.Markup, StringComparison.OrdinalIgnoreCase);
        }

        // Every control on the panel is accounted for, by name and then by count. The
        // count is the part that bites: a control added later without a reason lands
        // here rather than on screen unnoticed.
        Assert.Single(pane.FindAll("[data-testid='dashboard-repository-filter'] select"));
        Assert.Equal(2, pane.FindAll("[data-testid='dashboard-window-filter'] button").Count);
        Assert.Equal(7, pane.FindAll("[data-testid$='-refresh']").Count);
        Assert.Single(pane.FindAll("[aria-label='Close dashboard']"));

        var controls = pane.FindAll("button, select, input, textarea");

        // One close, one repository select, two window buttons, seven refreshes.
        Assert.Equal(1 + 1 + 2 + 7, controls.Count);
    }

    /// <summary>
    /// The pane holds nothing that outlives being closed. A saved arrangement is the
    /// thing that falls out of step with the code, so there is none to save.
    /// </summary>
    [Fact]
    public void Closing_and_reopening_the_pane_returns_to_the_default_scope()
    {
        var productivity = new RecordingProductivityInsights();

        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(productivity));

        var first = context.Render<DashboardPane>();
        first.Find("[data-testid='dashboard-repository-filter'] select").Change("backlog-ide");

        productivity.Scopes.Clear();

        var second = context.Render<DashboardPane>();
        _ = second;

        Assert.All(productivity.Scopes, scope => Assert.True(scope.IsAllRepositories));
    }

    [Fact]
    public void The_close_button_reports_that_it_was_pressed_and_nothing_else()
    {
        using var context = Context();

        var closed = 0;
        var pane = context.Render<DashboardPane>(parameters =>
            parameters.Add(pane => pane.OnClose, () => closed++));

        pane.Find("[aria-label='Close dashboard']").Click();

        Assert.Equal(1, closed);
    }

    /// <summary>The shell finds the surface by this id and test id; renaming either
    /// would break the takeover without breaking the pane.</summary>
    [Fact]
    public void The_panel_keeps_the_identifiers_the_shell_addresses_it_by()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        var panel = pane.Find("[data-testid='dashboard-panel']");

        Assert.Equal("dashboard-pane", panel.Id);
        Assert.Equal("dashboard-title", panel.GetAttribute("aria-labelledby"));
    }

    /// <summary>
    /// The header is the library SectionHeader now, so what is worth holding is
    /// that it still renders this pane's own class names - app.css styles all five
    /// and did not move.
    /// </summary>
    [Fact]
    public void The_header_is_the_shared_component_wearing_this_panes_classes()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        var header = pane.Find(".dashboard-panel__header");

        SectionHeaderAdoptionTests.AssertPaneHeader(header, "dashboard-panel", "dashboard-title");
        SectionHeaderAdoptionTests.AssertPaneHeaderActions(header, "dashboard-panel");
        Assert.NotNull(header.QuerySelector(".dashboard-panel__header-actions button"));
    }

    private static BunitContext Context(Action<IServiceCollection>? configure = null)
    {
        var context = new BunitContext();

        _ = context.Services.AddUnavailableDashboard("backlog", "backlog-ide");
        configure?.Invoke(context.Services);

        return context;
    }

    private sealed class RecordingProductivityInsights : IProductivityInsights
    {
        public List<DashboardScope> Scopes { get; } = [];

        public int Invalidations { get; private set; }

        public Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            return Task.FromResult(InsightResult<ProductivityHeadline>.Unavailable("Not configured."));
        }

        public Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            return Task.FromResult(InsightResult<ProductivityScoreInsight>.Unavailable("Not configured."));
        }

        public Task<InsightResult<ProductivityTrend>> GetTrendAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            return Task.FromResult(InsightResult<ProductivityTrend>.Unavailable("Not configured."));
        }

        public Task<InsightResult<ReworkInsight>> GetReworkAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            return Task.FromResult(InsightResult<ReworkInsight>.Unavailable("Not configured."));
        }

        public void Invalidate(DashboardScope scope) => Invalidations++;
    }

    /// <summary>
    /// Productivity that yields before answering, so the continuation genuinely
    /// resumes rather than running through synchronously. Every real provider behaves
    /// this way; a double returning <c>Task.FromResult</c> does not, which is what let
    /// a dispatcher bug through.
    /// </summary>
    private sealed class YieldingProductivityInsights : IProductivityInsights
    {
        public async Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return InsightResult<ProductivityHeadline>.Unavailable("Answered after yielding.");
        }

        public async Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return InsightResult<ProductivityScoreInsight>.Unavailable("Answered after yielding.");
        }

        public async Task<InsightResult<ProductivityTrend>> GetTrendAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return InsightResult<ProductivityTrend>.Unavailable("Answered after yielding.");
        }

        public async Task<InsightResult<ReworkInsight>> GetReworkAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return InsightResult<ReworkInsight>.Unavailable("Answered after yielding.");
        }

        public void Invalidate(DashboardScope scope)
        {
        }
    }

    private sealed class RecordingCostInsights : ICostInsights
    {
        public int Calls { get; private set; }

        public Task<InsightResult<SpendThisMonthInsight>> GetThisMonthAsync(
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(InsightResult<SpendThisMonthInsight>.Unavailable("Not configured."));
        }

        public Task<InsightResult<SpendTrendInsight>> GetTrendAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(InsightResult<SpendTrendInsight>.Unavailable("Not configured."));
        }

        public Task<InsightResult<SpendByModelInsight>> GetByModelAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(InsightResult<SpendByModelInsight>.Unavailable("Not configured."));
        }

        public void Invalidate()
        {
        }
    }

    /// <summary>Cost that can answer, so a test can prove one refused source does not
    /// silence a working one.</summary>
    private sealed class ReadyCostInsights : ICostInsights
    {
        public Task<InsightResult<SpendThisMonthInsight>> GetThisMonthAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendThisMonthInsight>.Ready(new SpendThisMonthInsight(
            [
                new MonthlySpend(
                    SpendProvider.Claude,
                    new DashboardMoney(12.34m, "USD"),
                    Allowance: null,
                    new DateOnly(2026, 8, 1),
                    new DateOnly(2026, 8, 19),
                    IsEstimate: true)
            ])));

        public Task<InsightResult<SpendTrendInsight>> GetTrendAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendTrendInsight>.Ready(new SpendTrendInsight(
                [new InsightSeries("Claude", [new InsightPoint("Aug 26", 12.34m)])],
                "USD",
                SpendBucket.Month)));

        public Task<InsightResult<SpendByModelInsight>> GetByModelAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByModelInsight>.Ready(new SpendByModelInsight(
                [new InsightRow("opus", 1_000, new DashboardMoney(12.34m, "USD"), "Claude")])));

        public void Invalidate()
        {
        }
    }
}
