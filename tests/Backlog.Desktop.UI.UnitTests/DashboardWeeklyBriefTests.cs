using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI;
using Backlog.Modules.Dashboard.UI.Parts;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The weekly brief, reached from the strip's "Read as brief" and left by "Open
/// overview": the last completed week, four templated blocks in the board's order, and
/// the Needs-you items carried into next week.
/// </summary>
public class DashboardWeeklyBriefTests
{
    /// <summary>A Wednesday in ISO week 42 of 2026, so the brief reports week 41.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] Blocks =
    [
        "dashboard-brief-shipping",
        "dashboard-brief-plan",
        "dashboard-brief-sessions",
        "dashboard-brief-cost"
    ];

    [Fact]
    public void Read_as_brief_shows_the_brief_in_place_of_the_tabs_and_open_overview_brings_them_back()
    {
        using var context = Context();
        var pane = context.Render<DashboardPane>();

        pane.Find("[data-testid='dashboard-tab-cost']").Click();
        pane.Find("[data-testid='dashboard-brief-link']").Click();

        Assert.Empty(pane.FindAll("[data-testid='dashboard-tablist']"));
        Assert.Empty(pane.FindAll("[data-testid='dashboard-window-filter']"));
        var brief = pane.Find("[data-testid='dashboard-brief']");
        Assert.Equal("Week 41 · 05 Oct – 11 Oct", pane.Find("[data-testid='dashboard-brief-week']").TextContent);

        // The blocks in the board's order, then what is carried into next week.
        Assert.Equal(
            [.. Blocks, "dashboard-brief-carry"],
            brief.Children.Skip(1).Select(child => child.GetAttribute("data-testid")));
        Assert.Contains("Carrying into next week", pane.Find("[data-testid='dashboard-brief-carry']").TextContent, StringComparison.Ordinal);

        pane.Find("[data-testid='dashboard-brief-overview']").Click();

        Assert.Empty(pane.FindAll("[data-testid='dashboard-brief']"));
        Assert.Equal("true", pane.Find("[data-testid='dashboard-tab-overview']").GetAttribute("aria-selected"));
    }

    /// <summary>
    /// Each block's sentence is its template filled from the week's figure and the one
    /// before it, beside that figure and eight weeks of bars ending on the reported week,
    /// which is the highlighted one.
    /// </summary>
    [Fact]
    public void Each_block_fills_its_template_from_the_reported_week_and_draws_eight_weeks()
    {
        using var context = Context(ready: true);
        var pane = context.Render<DashboardPane>();

        pane.Find("[data-testid='dashboard-brief-link']").Click();

        // Week 41 is the second-last point of every series: 41 against 40.
        Assert.Equal(
            "41 pull requests merged — 1 more than last week.",
            pane.Find("[data-testid='dashboard-brief-shipping-headline']").TextContent);
        Assert.Equal(
            "41 story points done — 1 more than last week.",
            pane.Find("[data-testid='dashboard-brief-plan-headline']").TextContent);
        Assert.Equal(
            "41.0h of agent time — 1.0h more than last week.",
            pane.Find("[data-testid='dashboard-brief-sessions-headline']").TextContent);
        Assert.Equal(
            "$41 spent — $1 more than last week.",
            pane.Find("[data-testid='dashboard-brief-cost-headline']").TextContent);

        Assert.Equal("41 issues closed, and no merge that week was reviewed.", pane.Find("[data-testid='dashboard-brief-shipping-detail']").TextContent);

        // W41 merged the most of the weeks read, and no provider passes a budget.
        pane.WaitForAssertion(() =>
            Assert.Equal("Your best shipping week this quarter.", pane.Find("[data-testid='dashboard-brief-title']").TextContent.Trim()));
        Assert.Equal("The roadmap is switched off.", pane.Find("[data-testid='dashboard-brief-plan-detail']").TextContent);
        Assert.Equal("No monthly budget is set.", pane.Find("[data-testid='dashboard-brief-cost-detail']").TextContent);

        foreach (var block in Blocks)
        {
            var columns = pane.FindAll($"[data-testid='{block}-chart'] .metric-bars__column");
            Assert.Equal(BriefWeek.Bars, columns.Count);
            Assert.Contains("metric-bars__column--current", columns[^1].ClassName, StringComparison.Ordinal);
            Assert.Contains("W41", columns[^1].GetAttribute("title"), StringComparison.Ordinal);
            Assert.NotNull(pane.Find($"[data-testid='{block}-figure']"));
        }
    }

    /// <summary>The week before is a step back over what was read, as far as the read
    /// reaches; the week after steps forward again.</summary>
    [Fact]
    public void The_week_before_steps_back_until_the_read_runs_out()
    {
        using var context = Context(ready: true);
        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-brief-link']").Click();

        Assert.Empty(pane.FindAll("[data-testid='dashboard-brief-later']"));

        pane.Find("[data-testid='dashboard-brief-earlier']").Click();

        Assert.StartsWith("Week 40 ·", pane.Find("[data-testid='dashboard-brief-week']").TextContent, StringComparison.Ordinal);
        Assert.Equal(
            "40 pull requests merged — 1 more than last week.",
            pane.Find("[data-testid='dashboard-brief-shipping-headline']").TextContent);

        for (var step = 1; step < BriefWeek.MaxWeeksBack; step++)
        {
            pane.Find("[data-testid='dashboard-brief-earlier']").Click();
        }

        Assert.StartsWith("Week 37 ·", pane.Find("[data-testid='dashboard-brief-week']").TextContent, StringComparison.Ordinal);
        Assert.Empty(pane.FindAll("[data-testid='dashboard-brief-earlier']"));

        pane.Find("[data-testid='dashboard-brief-later']").Click();

        Assert.StartsWith("Week 38 ·", pane.Find("[data-testid='dashboard-brief-week']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>A block whose source cannot answer says so in one line in place of its
    /// sentences, and draws no figure — the others stand.</summary>
    [Fact]
    public void A_block_whose_source_is_unavailable_says_so_in_one_line()
    {
        using var context = Context();
        var pane = context.Render<DashboardPane>();

        pane.Find("[data-testid='dashboard-brief-link']").Click();

        foreach (var (block, tag) in Blocks.Zip(["shipping", "plan", "sessions", "cost"]))
        {
            var missing = pane.Find($"[data-testid='{block}-missing']");
            Assert.Equal(
                $"No {tag} figures for this week: {DashboardTestHost.UnavailableReason}",
                missing.TextContent.Trim());
            Assert.Empty(pane.FindAll($"[data-testid='{block}-headline']"));
            Assert.Empty(pane.FindAll($"[data-testid='{block}-figure']"));
        }
    }

    /// <summary>The brief closes with the Overview's Needs-you items under "Carrying into
    /// next week", and a block's link opens its tab.</summary>
    [Fact]
    public void Carrying_into_next_week_holds_the_needs_you_items_and_a_block_links_to_its_tab()
    {
        using var context = Context(ready: true);
        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-brief-link']").Click();

        var item = Assert.Single(pane.FindAll("[data-testid='dashboard-brief-carry-item']"));
        Assert.Contains("Multi-account support", item.TextContent, StringComparison.Ordinal);
        Assert.Contains("6 pts left", item.TextContent, StringComparison.Ordinal);

        pane.Find("[data-testid='dashboard-brief-plan-open']").Click();

        Assert.Empty(pane.FindAll("[data-testid='dashboard-brief']"));
        Assert.Equal("true", pane.Find("[data-testid='dashboard-tab-tasks']").GetAttribute("aria-selected"));
    }

    /// <summary>
    /// A sprint series shown in the weekly brief puts a Sprint block first, before
    /// Shipping: the series, its repositories and the week of the sprint; the templated
    /// sentence; the figures; what shipped by plan, with the merged pull requests linked;
    /// and what carries into the next week.
    /// </summary>
    [Fact]
    public void A_sprint_shown_in_the_weekly_brief_puts_its_block_first()
    {
        var source = new SprintSource([SprintFixtures.Sprint42()]);
        using var context = Context(sprints: source);
        var pane = context.Render<DashboardPane>();

        pane.Find("[data-testid='dashboard-brief-link']").Click();

        var brief = pane.Find("[data-testid='dashboard-brief']");
        Assert.Equal(
            ["dashboard-brief-sprint", .. Blocks, "dashboard-brief-carry"],
            brief.Children.Skip(1).Select(child => child.GetAttribute("data-testid")));

        // Every week the brief can step to, read once: weeks 37 to 41.
        Assert.Equal([(new DateOnly(2026, 9, 7), new DateOnly(2026, 10, 11))], source.Asked);

        Assert.Equal("Sprint 42", pane.Find("[data-testid='dashboard-brief-sprint-name']").TextContent);
        Assert.Equal(
            "Fincent team · Fincent, devbook · week 1 of 2 · 05 Oct – 18 Oct",
            pane.Find("[data-testid='dashboard-brief-sprint-meta']").TextContent);
        Assert.StartsWith(
            "2 of 4 goal tasks and 3 of 6 items done after the first week",
            pane.Find("[data-testid='dashboard-brief-sprint-summary']").TextContent,
            StringComparison.Ordinal);

        Assert.Equal("2 of 4", Value("goal"));
        Assert.Equal("3", Value("done"));
        Assert.Equal("6", Value("items"));
        Assert.Contains("+1 added", pane.Find("[data-testid='dashboard-brief-sprint-figure-items'] dt").TextContent, StringComparison.Ordinal);
        Assert.Equal("3", Value("merged"));
        Assert.Equal("1", Value("blocked"));

        Assert.Equal(
            ["+feature-flags", "+rekeningen", "+specs"],
            pane.FindAll("[data-testid='dashboard-brief-sprint-plan-name']").Select(name => name.TextContent));
        Assert.Equal(
            ["https://github.com/innovadis-dev/Fincent/pull/4560", "https://github.com/innovadis-dev/Fincent/pull/4558", "https://github.com/innovadis-dev/Fincent/pull/4530"],
            pane.FindAll("a[data-testid='dashboard-brief-sprint-pull']").Select(link => link.GetAttribute("href")));

        Assert.Equal("Into week 2", pane.Find("[data-testid='dashboard-brief-sprint-into-heading']").TextContent);
        Assert.Equal(
            "3 open going into the second week, Openspec blocked among them. Local dev platform #4572 waits on review.",
            pane.Find("[data-testid='dashboard-brief-sprint-into-detail']").TextContent);

        // A step back moves what the block draws, never what it fetches: week 40 is
        // before the sprint, so the block goes.
        pane.Find("[data-testid='dashboard-brief-earlier']").Click();

        Assert.Empty(pane.FindAll("[data-testid='dashboard-brief-sprint']"));
        Assert.Single(source.Asked);

        string Value(string figure) =>
            pane.Find($"[data-testid='dashboard-brief-sprint-figure-{figure}'] dd").TextContent;
    }

    /// <summary>With no series shown — Progress off, or none marked — the port answers
    /// with no sprint and the brief is exactly the one it was.</summary>
    [Fact]
    public void With_no_sprint_to_show_the_brief_is_unchanged()
    {
        using var context = Context(sprints: new SprintSource([]));
        var pane = context.Render<DashboardPane>();

        pane.Find("[data-testid='dashboard-brief-link']").Click();

        Assert.Equal(
            [.. Blocks, "dashboard-brief-carry"],
            pane.Find("[data-testid='dashboard-brief']").Children.Skip(1).Select(child => child.GetAttribute("data-testid")));
        Assert.Empty(pane.FindAll("[data-testid='dashboard-brief-sprint']"));
    }

    /// <summary>The repository filter keeps a series with a repository in focus.</summary>
    [Fact]
    public void A_sprint_outside_the_repositories_in_focus_draws_no_block()
    {
        using var context = Context(sprints: new SprintSource([SprintFixtures.Sprint42()]));
        var brief = context.Render<WeeklyBrief>(parameters => parameters
            .Add(component => component.Scope, DashboardScope.Default with { Repositories = RepositoryFocus.Of("tools") }));

        Assert.Empty(brief.FindAll("[data-testid='dashboard-brief-sprint']"));
    }

    private sealed class SprintSource(IReadOnlyList<SprintBriefSprint> sprints) : ISprintBriefSource
    {
        public event Action? Changed { add { } remove { } }

        public List<(DateOnly From, DateOnly To)> Asked { get; } = [];

        public Task<IReadOnlyList<SprintBriefSprint>> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Asked.Add((from, to));
            return Task.FromResult(sprints);
        }
    }

    private static BunitContext Context(bool ready = false, ISprintBriefSource? sprints = null)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        _ = context.Services.AddUnavailableDashboard("backlog");
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));

        if (sprints is not null) context.Services.AddSingleton(sprints);

        if (ready)
        {
            context.Services.AddSingleton<IProductivityInsights>(new ReadyProductivity());
            context.Services.AddSingleton<ITaskInsights>(new ReadyTasks());
            context.Services.AddSingleton<ISessionInsights>(new ReadySessions());
            context.Services.AddSingleton<ICostInsights>(new ReadyCost());
            context.Services.AddSingleton<IAttentionInsights>(new OneItemAttention());
        }

        return context;
    }

    /// <summary>Thirteen weeks, W30 to the running W42, each worth its own number.</summary>
    private static IReadOnlyList<InsightPoint> Weeks() =>
        [.. Enumerable.Range(30, 13).Select(week => new InsightPoint($"W{week}", week))];

    private sealed class ReadyProductivity : IProductivityInsights
    {
        public Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityHeadline>.Ready(
                new ProductivityHeadline(450, 450, 0m, null, Weeks(), Weeks(), [])));

        public Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityScoreInsight>.Unavailable(DashboardTestHost.UnavailableReason));

        public Task<InsightResult<ReworkInsight>> GetReworkAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ReworkInsight>.Unavailable(DashboardTestHost.UnavailableReason));

        public void Invalidate(DashboardScope scope)
        {
        }
    }

    private sealed class ReadyTasks : ITaskInsights
    {
        public Task<InsightResult<TaskThroughputInsight>> GetThroughputAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<TaskThroughputInsight>.Ready(new TaskThroughputInsight(Weeks(), Weeks(), 0)));

        public Task<InsightResult<PlanInsight>> GetPlanAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<PlanInsight>.Ready(PlanInsight.Off));
    }

    private sealed class ReadySessions : ISessionInsights
    {
        public Task<InsightResult<AssistantSessionsInsight>> GetSessionsAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<AssistantSessionsInsight>.Ready(
                AssistantSessionsInsight.Empty with { ActiveTimePerCalendarWeek = Weeks(), SessionsPerCalendarWeek = Weeks() }));

        public void Invalidate()
        {
        }
    }

    private sealed class ReadyCost : ICostInsights
    {
        public Task<InsightResult<SpendByWeekInsight>> GetByWeekAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByWeekInsight>.Ready(new SpendByWeekInsight(Weeks(), "USD", false)));

        public Task<InsightResult<SpendProjectionInsight>> GetProjectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendProjectionInsight>.Ready(new SpendProjectionInsight([])));

        public Task<InsightResult<SpendThisMonthInsight>> GetThisMonthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendThisMonthInsight>.Unavailable(DashboardTestHost.UnavailableReason));

        public Task<InsightResult<SpendByModelInsight>> GetByModelAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByModelInsight>.Unavailable(DashboardTestHost.UnavailableReason));

        public Task<InsightResult<SpendByDayInsight>> GetByDayAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByDayInsight>.Unavailable(DashboardTestHost.UnavailableReason));

        public void Invalidate()
        {
        }
    }

    private sealed class OneItemAttention : IAttentionInsights
    {
        public Task<NeedsYouInsight> GetNeedsYouAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NeedsYouInsight(
                [new NeedsYouItem(NeedsYouRule.RoadmapBehind, "Multi-account support", "Behind pace: 6 pts left.", NeedsYouTab.Tasks)],
                []));
    }
}
