using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The pane's header and shell (board Main; AC1–AC5): the tab's eyebrow and sentence, the
/// four-window radio group beside the ✕, the tabs whose figures the window and machine
/// do not reach, and the layout guards the harness measures at phone width.
/// </summary>
public sealed class DashboardPaneHeaderTests
{
    /// <summary>Monday 12 October 2026, ISO week 42.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_window_is_one_choice_of_four_weeks()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        var group = pane.Find("[data-testid='dashboard-window-filter']");

        Assert.Equal("radiogroup", group.GetAttribute("role"));
        Assert.Equal(
            ["4 weeks", "8 weeks", "12 weeks", "26 weeks"],
            group.QuerySelectorAll("[role='radio']").Select(radio => radio.TextContent.Trim()));
        Assert.Equal("true", pane.Find("[data-testid='dashboard-window-12']").GetAttribute("aria-checked"));
    }

    [Fact]
    public void Choosing_twenty_six_weeks_moves_every_part_to_it()
    {
        var productivity = new Scopes();
        using var context = Context(services => services.AddSingleton<IProductivityInsights>(productivity));

        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-window-26']").Click();

        pane.WaitForAssertion(() => Assert.Contains(DashboardPeriod.TwentySixWeeks, productivity.Asked.Select(scope => scope.Period)));
        Assert.Equal("true", pane.Find("[data-testid='dashboard-window-26']").GetAttribute("aria-checked"));
    }

    [Fact]
    public void The_overview_header_names_the_week_the_repositories_and_the_window()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();

        Assert.Equal("Dashboard · Overview", pane.Find(".dashboard-panel__eyebrow").TextContent.Trim());
        pane.WaitForAssertion(() =>
            Assert.Equal("Week 42 · 2 repositories, last 12 weeks", pane.Find("#dashboard-title").TextContent.Trim()));
    }

    /// <summary>A tab whose insight cannot answer names the tab rather than going blank;
    /// the eyebrow always names it.</summary>
    [Fact]
    public void A_tab_whose_insight_cannot_answer_is_named_instead()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-tab-productivity']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal("Dashboard · Productivity", pane.Find(".dashboard-panel__eyebrow").TextContent.Trim());
            Assert.Equal("Productivity", pane.Find("#dashboard-title").TextContent.Trim());
        });
    }

    [Fact]
    public void A_tab_whose_insight_answers_leads_with_its_sentence()
    {
        using var context = Context(services =>
            services.AddSingleton<IProductivityInsights>(new Scopes(new ProductivityHeadline(143, 0, 0.17m, null, [], [], []))));

        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-tab-productivity']").Click();

        pane.WaitForAssertion(() =>
            Assert.Equal("143 pull requests merged, 17% came back", pane.Find("#dashboard-title").TextContent.Trim()));
    }

    /// <summary>A part's refresh moves the header too: it never keeps a sentence the tab
    /// under it no longer shows (review, Batch B).</summary>
    [Fact]
    public void Refreshing_a_part_moves_the_headline()
    {
        var productivity = new Scopes(new ProductivityHeadline(143, 0, 0.17m, null, [], [], []));
        using var context = Context(services => services.AddSingleton<IProductivityInsights>(productivity));

        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-tab-productivity']").Click();
        pane.WaitForAssertion(() =>
            Assert.Equal("143 pull requests merged, 17% came back", pane.Find("#dashboard-title").TextContent.Trim()));

        productivity.Headline = new ProductivityHeadline(150, 0, 0.2m, null, [], [], []);
        pane.Find("[data-testid='dashboard-headline-refresh']").Click();

        pane.WaitForAssertion(() =>
            Assert.Equal("150 pull requests merged, 20% came back", pane.Find("#dashboard-title").TextContent.Trim()));
    }

    [Fact]
    public void A_header_that_fell_back_to_the_tab_name_reads_again_when_a_part_refreshes()
    {
        var productivity = new Scopes();
        using var context = Context(services => services.AddSingleton<IProductivityInsights>(productivity));

        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-tab-productivity']").Click();
        pane.WaitForAssertion(() => Assert.Equal("Productivity", pane.Find("#dashboard-title").TextContent.Trim()));

        productivity.Headline = new ProductivityHeadline(9, 0, 0m, null, [], [], []);
        pane.Find("[data-testid='dashboard-headline-refresh']").Click();

        pane.WaitForAssertion(() =>
            Assert.Equal("9 pull requests merged, 0% came back", pane.Find("#dashboard-title").TextContent.Trim()));
    }

    /// <summary>On the Devbook tab the window and the machine reach nothing, and the
    /// header says so beside two disabled controls; on any other tab both come back
    /// (AC3).</summary>
    [Fact]
    public void The_devbook_tab_says_the_window_and_machine_do_not_apply_and_disables_them()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-tab-devbook']").Click();

        pane.WaitForAssertion(() =>
            Assert.Equal("Window and machine don't apply", pane.Find("[data-testid='dashboard-scope-note']").TextContent.Trim()));
        Assert.All(pane.FindAll("[data-testid='dashboard-window-filter'] button"), button => Assert.True(button.HasAttribute("disabled")));
        Assert.True(pane.Find("[data-testid='dashboard-machine-filter'] select").HasAttribute("disabled"));

        pane.Find("[data-testid='dashboard-tab-overview']").Click();

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='dashboard-scope-note']")));
        Assert.All(pane.FindAll("[data-testid='dashboard-window-filter'] button"), button => Assert.False(button.HasAttribute("disabled")));
        Assert.False(pane.Find("[data-testid='dashboard-machine-filter'] select").HasAttribute("disabled"));
    }

    /// <summary>The Cost tab reads the calendar month, so it names the month where the
    /// window would be, and disables the two controls (decision Q7).</summary>
    [Fact]
    public void The_cost_tab_names_the_month_and_disables_the_window_and_machine()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-tab-cost']").Click();

        pane.WaitForAssertion(() =>
            Assert.Equal("October", pane.Find("[data-testid='dashboard-scope-note']").TextContent.Trim()));
        Assert.All(pane.FindAll("[data-testid='dashboard-window-filter'] button"), button => Assert.True(button.HasAttribute("disabled")));
        Assert.True(pane.Find("[data-testid='dashboard-machine-filter'] select").HasAttribute("disabled"));
    }

    /// <summary>
    /// AC5's guards bUnit can hold: the tab strip keeps its tablist in one row that
    /// scrolls sideways rather than wrapping, and the Tasks split never asks a column for
    /// more than the panel has. The widths themselves are measured in the harness.
    /// </summary>
    [Fact]
    public void At_phone_width_the_strip_scrolls_in_one_row_and_the_tasks_split_does_not_clip()
    {
        var css = File.ReadAllText(RepositoryRoot.File("src", "Modules", "Dashboard", "Backlog.Modules.Dashboard.UI", "wwwroot", "dashboard.css")).Replace("\r\n", "\n");

        var strip = Block(css, ".dashboard-tabs__strip {");
        Assert.Contains("overflow-x: auto;", strip, StringComparison.Ordinal);

        var list = Block(css, ".dashboard-tabs__strip > .tabs__list {");
        Assert.Contains("flex-wrap: nowrap;", list, StringComparison.Ordinal);

        var split = Block(css, ".dashboard-section__split {");
        Assert.Contains("minmax(min(22rem, 100%), 1fr)", split, StringComparison.Ordinal);

        Assert.DoesNotContain(".dashboard-panel__filters", css, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verify measured the page itself scrolling 110 px sideways at 390 and 375: the
    /// Devbook tab's screen-reader badge text is absolutely positioned, and with no
    /// positioned ancestor inside the strip it escaped the strip's scroll box to the
    /// page's. The strip is its containing block, so the text scrolls with the tabs.
    /// </summary>
    [Fact]
    public void The_tab_strip_contains_its_absolutely_positioned_screen_reader_text()
    {
        var css = File.ReadAllText(RepositoryRoot.File("src", "Modules", "Dashboard", "Backlog.Modules.Dashboard.UI", "wwwroot", "dashboard.css")).Replace("\r\n", "\n");

        var strip = Block(css, ".dashboard-tabs__strip {");
        Assert.Contains("position: relative;", strip, StringComparison.Ordinal);
    }

    /// <summary>
    /// AC33: the brief's own narrative sentence is its visible title, and "Weekly brief"
    /// stays only as the pane's accessible name. Verify read it as a visible headline
    /// above the narrative title.
    /// </summary>
    [Fact]
    public void In_the_brief_the_pane_title_is_the_accessible_name_only()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        Assert.DoesNotContain("sr-only", pane.Find("#dashboard-title").ClassName ?? string.Empty, StringComparison.Ordinal);

        pane.Find("[data-testid='dashboard-brief-link']").Click();

        pane.WaitForAssertion(() =>
        {
            var title = pane.Find("#dashboard-title");
            Assert.Equal("Weekly brief", title.TextContent.Trim());
            Assert.Contains("sr-only", title.ClassList);
        });
    }

    /// <summary>
    /// Verify round 2 measured the Sessions body scrolling 41 px sideways at 390 and 375:
    /// the heatmap's longest legend entry and the "More: tokens, pull requests, peaks and
    /// machines" fold title did not wrap. Both may now break onto a second line.
    /// </summary>
    [Fact]
    public void The_heatmap_legend_and_the_more_fold_title_wrap_at_phone_width()
    {
        var dashboard = File.ReadAllText(RepositoryRoot.File("src", "Modules", "Dashboard", "Backlog.Modules.Dashboard.UI", "wwwroot", "dashboard.css")).Replace("\r\n", "\n");
        var components = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.css")).Replace("\r\n", "\n");

        var item = Block(components, ".metric-heatmap__legend-item {");
        Assert.DoesNotContain("white-space: nowrap;", item, StringComparison.Ordinal);
        Assert.Contains("min-width: 0;", item, StringComparison.Ordinal);

        var title = Block(dashboard, ".dashboard-section__more-title {");
        Assert.Contains("overflow-wrap: anywhere;", title, StringComparison.Ordinal);
        Assert.Contains("min-width: 0;", title, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verify round 3: the "More: tokens, pull requests, peaks and machines" fold button
    /// still overflowed by 41-56 px, because the library's fold label never wraps. The
    /// dashboard's fold, under its own base class, lets its label wrap. The library
    /// default is unchanged for every other host. The render checks that the selector
    /// reaches the label the fold actually draws.
    /// </summary>
    [Fact]
    public void The_dashboard_more_fold_label_wraps_and_other_folds_keep_theirs()
    {
        var dashboard = File.ReadAllText(RepositoryRoot.File("src", "Modules", "Dashboard", "Backlog.Modules.Dashboard.UI", "wwwroot", "dashboard.css")).Replace("\r\n", "\n");
        var components = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.css")).Replace("\r\n", "\n");

        var label = Block(dashboard, ".dashboard-section__more .fold__label {");
        Assert.Contains("white-space: normal;", label, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere;", label, StringComparison.Ordinal);
        Assert.Contains("max-width: 100%;", Block(dashboard, ".dashboard-section__more .fold__trigger {"), StringComparison.Ordinal);

        Assert.Contains("white-space: nowrap;", Block(components, ".fold__label {\n    min-width: 0;\n    overflow: hidden;"), StringComparison.Ordinal);

        using var context = new BunitContext();
        var fold = context.Render<Backlog.UI.Components.Layout.FoldControl>(parameters => parameters
            .Add(p => p.Label, "More: tokens, pull requests, peaks and machines")
            .Add(p => p.BaseClass, "dashboard-section__more"));

        Assert.NotNull(fold.Find(".dashboard-section__more .fold__trigger .fold__label"));
    }

    [Fact]
    public void The_tab_strip_keeps_its_tablist_inside_the_scrolling_row()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        var list = pane.Find("[data-testid='dashboard-tablist']");

        Assert.Equal("tablist", list.GetAttribute("role"));
        Assert.Contains("dashboard-tabs__strip", list.ParentElement!.ClassList);
    }

    private static BunitContext Context(Action<IServiceCollection>? configure = null)
    {
        var context = new BunitContext();
        _ = context.Services.AddUnavailableDashboard("backlog", "backlog-ide");
        var clock = new FakeTimeProvider(Now);
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        context.Services.AddSingleton<TimeProvider>(clock);
        configure?.Invoke(context.Services);
        return context;
    }

    private static string Block(string css, string opening)
    {
        var start = css.IndexOf(opening, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{opening} should exist.");

        var end = css.IndexOf('}', start);
        return css[start..(end + 1)];
    }

    private sealed class Scopes(ProductivityHeadline? headline = null) : IProductivityInsights
    {
        public ProductivityHeadline? Headline { get; set; } = headline;

        public List<DashboardScope> Asked { get; } = [];

        public Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(DashboardScope scope, CancellationToken cancellationToken = default)
        {
            Asked.Add(scope);
            return Task.FromResult(Headline is null
                ? InsightResult<ProductivityHeadline>.Unavailable("Not configured.")
                : InsightResult<ProductivityHeadline>.Ready(Headline));
        }

        public Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(DashboardScope scope, CancellationToken cancellationToken = default)
        {
            Asked.Add(scope);
            return Task.FromResult(InsightResult<ProductivityScoreInsight>.Unavailable("Not configured."));
        }

        public Task<InsightResult<ReworkInsight>> GetReworkAsync(DashboardScope scope, CancellationToken cancellationToken = default)
        {
            Asked.Add(scope);
            return Task.FromResult(InsightResult<ReworkInsight>.Unavailable("Not configured."));
        }

        public void Invalidate(DashboardScope scope)
        {
        }
    }
}
