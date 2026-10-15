using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI;
using Backlog.SharedKernel.Ai;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Dashboard's Ask AI content: the figures every tab draws, one line per
/// insight and always kept, then the pull requests and issues behind them,
/// fetched for the scope the pane is showing.
/// </summary>
public class DashboardAiContentSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The figure lines, in the order the body carries them — the order
    /// the tabs run in.</summary>
    private static readonly string[] FigurePrefixes =
    [
        "Throughput:",
        "Score:",
        "Rework after review:",
        "Tasks:",
        "Plan:",
        "Assistant sessions:",
        "Hours worked:",
        "Devbook drift:",
        "Spend this month",
        "Spend by model",
        "Needs you:"
    ];

    [Fact]
    public async Task A_record_names_the_pull_request_or_issue_with_its_facts_newest_first()
    {
        var activity = new StubActivity
        {
            Report = new ActivityReport(
                [
                    PullRequest(410, "Older fix", Now.AddDays(-20)),
                    PullRequest(412, "Newer feature", Now.AddDays(-2)) with { FirstReviewedAt = Now.AddDays(-3), ReviewRounds = 2, SizeKnown = true, ChangedLines = 120, ChangedFiles = 4 }
                ],
                [new ActivityIssue("backlog", 77, Now.AddDays(-1)) { Title = "Chips reorder" }])
        };

        var content = await Source(activity).ComposeAsync(new AiContentRequest("no shared words here", 6000), TestContext.Current.CancellationToken);

        Assert.Equal("dashboard", content.AreaKey);
        Assert.Equal(3, content.Total);
        Assert.EndsWith(
            "PR #412: Newer feature — merged, backlog, 2026-09-20, 2 review rounds, 120 lines in 4 files\n"
            + "---\n"
            + "PR #410: Older fix — merged, backlog, 2026-09-02, not reviewed\n"
            + "---\n"
            + "Issue #77: Chips reorder — closed, backlog, 2026-09-21",
            content.Body,
            StringComparison.Ordinal);
    }

    /// <summary>One line per insight, in the tabs' order, worded the way the parts
    /// word them, and all of them before the first pull request.</summary>
    [Fact]
    public async Task The_figures_of_every_tab_come_first_one_line_each_in_a_fixed_order()
    {
        var activity = new StubActivity { Report = new ActivityReport([PullRequest(5, "Shipped", Now.AddDays(-1))], []) };

        var content = await Source(activity).ComposeAsync(new AiContentRequest("anything", 6000), TestContext.Current.CancellationToken);

        var lines = Lines(content.Body);
        var positions = FigurePrefixes.Select(prefix => lines.FindIndex(line => line.StartsWith(prefix, StringComparison.Ordinal))).ToList();

        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
        Assert.True(positions[^1] < lines.FindIndex(line => line.StartsWith("PR #5", StringComparison.Ordinal)));

        Assert.Contains("Throughput: pull requests merged 12 (1 a week); issues closed 5", content.Body, StringComparison.Ordinal);
        Assert.Contains("came back after review 25% of the reviewed", content.Body, StringComparison.Ordinal);
        Assert.Contains("Score: volume 68.4 of 100; quality 72 of 100", content.Body, StringComparison.Ordinal);
        Assert.Contains("Tasks: tasks completed 9", content.Body, StringComparison.Ordinal);
        Assert.Contains("Plan: 6 of 20 points done, 1 behind pace", content.Body, StringComparison.Ordinal);
        Assert.Contains("behind: Sync rewrite", content.Body, StringComparison.Ordinal);
        Assert.Contains("Assistant sessions: 64 sessions, 38 h of agent time", content.Body, StringComparison.Ordinal);
        Assert.Contains("Hours worked: worked 42.5h; inside office hours 38.0h; outside office hours 4.5h; planned 40.0h", content.Body, StringComparison.Ordinal);
        Assert.Contains("Devbook drift: 1 of 2 units agree with the code", content.Body, StringComparison.Ordinal);
        Assert.Contains("$212 this month, heading for $312", content.Body, StringComparison.Ordinal);
        Assert.Contains("Opus $180", content.Body, StringComparison.Ordinal);
        Assert.Contains("Needs you: Roadmap behind — Sync rewrite is 2 weeks late", content.Body, StringComparison.Ordinal);
    }

    /// <summary>The figures are charged first and always go: with more pull
    /// requests than the budget holds, and a question whose words are all in the
    /// pull requests, the pull requests are what is trimmed.</summary>
    [Fact]
    public async Task Overflowing_pull_requests_are_trimmed_while_every_figure_line_is_kept_within_the_budget()
    {
        const int budget = 3000;
        var activity = new StubActivity
        {
            Report = new ActivityReport(
                [.. Enumerable.Range(1, 200).Select(number => PullRequest(number, "Refactor the sync engine", Now.AddHours(-number)))],
                [])
        };

        var content = await Source(activity).ComposeAsync(new AiContentRequest("refactor sync engine", budget), TestContext.Current.CancellationToken);

        Assert.True(content.Body.Length <= budget, $"The body is {content.Body.Length} characters, over {budget}.");
        Assert.True(content.Trimmed);
        var shown = Lines(content.Body).Count(line => line.StartsWith("PR #", StringComparison.Ordinal));
        Assert.InRange(shown, 1, 199);
        Assert.Equal(
            $"Dashboard (12 weeks, every repository): {shown} of 200 entries, selected by relevance to the question.",
            Lines(content.Body)[0]);
        Assert.All(FigurePrefixes, prefix => Assert.Contains(Lines(content.Body), line => line.StartsWith(prefix, StringComparison.Ordinal)));
    }

    /// <summary>An insight that cannot answer is said, in its place, and takes no
    /// other figure with it.</summary>
    [Fact]
    public async Task An_unavailable_insight_is_a_line_saying_so_and_the_others_are_still_sent()
    {
        var insights = new StubInsights { HoursWorked = InsightResult<HoursWorkedInsight>.Unavailable("No working week is set.") };

        var content = await Source(new StubActivity(), insights: insights).ComposeAsync(new AiContentRequest("anything", 6000), TestContext.Current.CancellationToken);

        Assert.Contains("Hours worked: unavailable: No working week is set.", Lines(content.Body));
        Assert.Contains("Throughput: pull requests merged 12", content.Body, StringComparison.Ordinal);
        Assert.Contains("$212 this month", content.Body, StringComparison.Ordinal);
    }

    /// <summary>Without GitHub the pull requests are not fetched, and the body says
    /// so — but the tasks, sessions, hours and spend do not come from GitHub and
    /// are still sent.</summary>
    [Fact]
    public async Task Without_github_nothing_is_fetched_and_the_figures_that_do_not_need_it_are_still_sent()
    {
        var activity = new StubActivity { Unavailable = "GitHub is not signed in." };
        var insights = new StubInsights
        {
            Headline = InsightResult<ProductivityHeadline>.Unavailable("GitHub is not signed in."),
            Score = InsightResult<ProductivityScoreInsight>.Unavailable("GitHub is not signed in."),
            Rework = InsightResult<ReworkInsight>.Unavailable("GitHub is not signed in.")
        };

        var content = await Source(activity, insights: insights).ComposeAsync(new AiContentRequest("anything", 6000), TestContext.Current.CancellationToken);

        Assert.Empty(activity.Fetches);
        Assert.Contains("GitHub is unavailable: GitHub is not signed in. No pull request or issue is included.", Lines(content.Body));
        Assert.Contains("Throughput: unavailable: GitHub is not signed in.", Lines(content.Body));
        Assert.Contains("Tasks: tasks completed 9", content.Body, StringComparison.Ordinal);
        Assert.Contains("Assistant sessions: 64 sessions", content.Body, StringComparison.Ordinal);
        Assert.Contains("Hours worked: worked 42.5h", content.Body, StringComparison.Ordinal);
        Assert.Contains("$212 this month", content.Body, StringComparison.Ordinal);
    }

    /// <summary>The scope the pane published is the scope the fetch is made
    /// for: its repositories and its window, not every repository over a
    /// quarter. The scope is content — it defines which facts this area has —
    /// and the first line names it.</summary>
    [Fact]
    public async Task The_fetch_and_the_first_line_follow_the_scope_the_pane_is_showing()
    {
        var activity = new StubActivity();
        var scope = new DashboardScopeInView();
        scope.Set(new DashboardScope(RepositoryFocus.Of("docs"), DashboardPeriod.FourWeeks));

        _ = await Source(activity, scope).ComposeAsync(new AiContentRequest("anything", 6000), TestContext.Current.CancellationToken);

        var fetched = Assert.Single(activity.Fetches);
        Assert.Equal(["docs"], fetched.Repositories.Select(repository => repository.Alias));
        Assert.Equal(Now, fetched.To);
        Assert.Equal(new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero), fetched.From);
    }

    /// <summary>Every insight that takes a scope is asked for the pane's, never the
    /// dashboard's default; spend takes none and is the calendar month, which its
    /// line names.</summary>
    [Fact]
    public async Task The_figures_are_read_for_the_scope_the_pane_is_showing_and_spend_names_its_month()
    {
        var insights = new StubInsights();
        var scope = new DashboardScopeInView();
        var shown = new DashboardScope(RepositoryFocus.Of("docs"), DashboardPeriod.FourWeeks, "laptop");
        scope.Set(shown);

        var content = await Source(new StubActivity(), scope, insights).ComposeAsync(new AiContentRequest("anything", 6000), TestContext.Current.CancellationToken);

        Assert.Equal(9, insights.Scopes.Count);
        Assert.All(insights.Scopes, asked => Assert.Equal(shown, asked));
        Assert.Contains(Lines(content.Body), line => line.StartsWith("Spend this month (September 2026", StringComparison.Ordinal));
        Assert.Contains("Throughput: pull requests merged 12 (3 a week)", content.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_first_line_names_the_scope_and_an_incomplete_report_is_said_under_it()
    {
        var activity = new StubActivity
        {
            Report = new ActivityReport([PullRequest(1, "Only one", Now.AddDays(-1))], []) { Complete = false }
        };
        var scope = new DashboardScopeInView();
        scope.Set(new DashboardScope(RepositoryFocus.Of("docs"), DashboardPeriod.FourWeeks));

        var content = await Source(activity, scope).ComposeAsync(new AiContentRequest("anything", 6000), TestContext.Current.CancellationToken);

        var lines = Lines(content.Body);
        Assert.Equal("Dashboard (4 weeks, docs): 1 entry.", lines[0]);
        Assert.StartsWith("First, 11 lines of the dashboard's figures", lines[1], StringComparison.Ordinal);
        Assert.Equal("One or more repositories could not be read.", lines[2]);
        Assert.StartsWith("Throughput:", lines[3], StringComparison.Ordinal);
    }

    /// <summary>The first line counts what the assistant would count — the pull
    /// requests and issues — and the line under it says the figure lines above
    /// them are not entries, so a count of the body cannot become eleven too many.</summary>
    [Fact]
    public async Task The_first_line_counts_only_pull_requests_and_issues_and_the_figures_are_named_apart()
    {
        var activity = new StubActivity
        {
            Report = new ActivityReport(
                [PullRequest(1, "One", Now.AddDays(-1)), PullRequest(2, "Two", Now.AddDays(-2))],
                [new ActivityIssue("backlog", 3, Now.AddDays(-3)) { Title = "Three" }])
        };

        var content = await Source(activity).ComposeAsync(new AiContentRequest("anything", 6000), TestContext.Current.CancellationToken);

        var lines = Lines(content.Body);
        Assert.Equal("Dashboard (12 weeks, every repository): 3 entries.", lines[0]);
        Assert.Equal(
            "First, 11 lines of the dashboard's figures, one per part across every tab; they are not entries. "
            + "Any entries after them are merged pull requests and closed issues.",
            lines[1]);
        Assert.Equal(3, content.Shown);
        Assert.Equal(3, content.Total);
        Assert.False(content.Trimmed);
    }

    private static List<string> Lines(string body) => [.. body.Split('\n')];

    private static DashboardAiContentSource Source(StubActivity activity, DashboardScopeInView? scope = null, StubInsights? insights = null)
    {
        insights ??= new StubInsights();

        return new(
            activity,
            new TwoRepositories(),
            scope ?? new DashboardScopeInView(),
            new DashboardInsightPorts(insights, insights, insights, insights, insights, insights, insights),
            new FakeTimeProvider(Now));
    }

    private static ActivityPullRequest PullRequest(int number, string title, DateTimeOffset mergedAt) =>
        new("backlog", number, mergedAt, null, 0, 0, 0, 0, true) { Title = title };

    private sealed class TwoRepositories : IRepositoryDirectory
    {
        public IReadOnlyList<DashboardRepository> Repositories { get; } =
            [new("backlog", "JSdotNet/Backlog"), new("docs", "JSdotNet/Docs")];
    }

    private sealed class StubActivity : IActivitySource
    {
        public ActivityReport Report { get; init; } = ActivityReport.Empty;

        public string? Unavailable { get; init; }

        public List<(IReadOnlyList<DashboardRepository> Repositories, DateTimeOffset From, DateTimeOffset To)> Fetches { get; } = [];

        public Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Unavailable is null ? InsightAvailability.Available : InsightAvailability.Unavailable(Unavailable));

        public Task<ActivityReport> GetActivityAsync(
            IReadOnlyList<DashboardRepository> repositories,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default)
        {
            Fetches.Add((repositories, from, to));
            return Task.FromResult(Report);
        }
    }

    /// <summary>Every insight the dashboard reads, answering one fixed reading
    /// each and keeping the scope each was asked for.</summary>
    private sealed class StubInsights :
        IProductivityInsights, ITaskInsights, ISessionInsights, IHoursWorkedInsights, IDriftInsights, ICostInsights, IAttentionInsights
    {
        private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

        public System.Collections.Concurrent.ConcurrentBag<DashboardScope> Scopes { get; } = [];

        public InsightResult<ProductivityHeadline> Headline { get; init; } = InsightResult<ProductivityHeadline>.Ready(
            new ProductivityHeadline(12, 5, 0.25m, TimeSpan.FromHours(3), [], [], []) { PullRequestsReviewed = 8, MedianCommitsPerPullRequest = 4 });

        public InsightResult<ProductivityScoreInsight> Score { get; init; } = InsightResult<ProductivityScoreInsight>.Ready(
            new ProductivityScoreInsight(
                new ProductivityScore(68.4m, [new ProductivityScoreInput("Merged pull requests", 12m, 20m)]),
                new ProductivityScore(72m, [new ProductivityScoreInput("Freedom from churn", 0.75m, 1m)])));

        public InsightResult<ReworkInsight> Rework { get; init; } = InsightResult<ReworkInsight>.Ready(
            new ReworkInsight(2, 8, 7, 1, 4, true, []) { ReviewRounds = 15, ChangesRequested = 2 });

        public InsightResult<TaskThroughputInsight> Throughput { get; init; } = InsightResult<TaskThroughputInsight>.Ready(
            new TaskThroughputInsight([new InsightPoint("W1", 4m), new InsightPoint("W2", 5m)], [new InsightPoint("W1", 8m), new InsightPoint("W2", 13m)], 1));

        public InsightResult<PlanInsight> Plan { get; init; } = InsightResult<PlanInsight>.Ready(
            new PlanInsight(
                true,
                new PlanPace(5m, PlanPaceBasis.LastFourWeeks),
                [
                    new PlanItemInsight(Item("Sync rewrite", 13, 3), PlanOutlook.Behind, Today.AddDays(14)),
                    new PlanItemInsight(Item("Chips", 7, 3), PlanOutlook.OnTrack, Today.AddDays(3))
                ]));

        public InsightResult<AssistantSessionsInsight> Sessions { get; init; } = InsightResult<AssistantSessionsInsight>.Ready(
            new AssistantSessionsInsight(64, TimeSpan.FromHours(38), Now, 0, false, [], []));

        public InsightResult<HoursWorkedInsight> HoursWorked { get; init; } = InsightResult<HoursWorkedInsight>.Ready(
            new HoursWorkedInsight(
                [new HoursWorkedPeriod(Today, Today, TimeSpan.FromHours(38), TimeSpan.FromHours(4.5), TimeSpan.FromHours(40))],
                []));

        public InsightResult<DriftInsight> Drift { get; init; } = InsightResult<DriftInsight>.Ready(
            new DriftInsight(
                [new DriftDirection("pull", [Unit("arc42", "aligned"), Unit("domain", "code-ahead")])],
                [new DriftIssue("backlog", 9, "[Devbook drift] domain", "https://example.test/9", false)],
                null));

        public InsightResult<SpendProjectionInsight> Projection { get; init; } = InsightResult<SpendProjectionInsight>.Ready(
            new SpendProjectionInsight(
            [
                new SpendProjection(SpendProvider.Claude, new DashboardMoney(212m, "USD"), new DashboardMoney(10m, "USD"),
                    new DashboardMoney(312m, "USD"), null, Today, 8, false)
            ]));

        public InsightResult<SpendByModelInsight> ByModel { get; init; } = InsightResult<SpendByModelInsight>.Ready(
            new SpendByModelInsight([new InsightRow("Opus", 1_200_000, new DashboardMoney(180m, "USD")), new InsightRow("Sonnet", 300_000, new DashboardMoney(32m, "USD"))]));

        public NeedsYouInsight NeedsYou { get; init; } = new(
            [new NeedsYouItem(NeedsYouRule.RoadmapBehind, "Roadmap behind", "Sync rewrite is 2 weeks late", NeedsYouTab.Tasks)],
            []);

        public Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(DashboardScope scope, CancellationToken cancellationToken = default) => Asked(scope, Headline);

        public Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(DashboardScope scope, CancellationToken cancellationToken = default) => Asked(scope, Score);

        public Task<InsightResult<ReworkInsight>> GetReworkAsync(DashboardScope scope, CancellationToken cancellationToken = default) => Asked(scope, Rework);

        public void Invalidate(DashboardScope scope)
        {
        }

        public Task<InsightResult<TaskThroughputInsight>> GetThroughputAsync(DashboardScope scope, CancellationToken cancellationToken = default) => Asked(scope, Throughput);

        public Task<InsightResult<PlanInsight>> GetPlanAsync(DashboardScope scope, CancellationToken cancellationToken = default) => Asked(scope, Plan);

        public Task<InsightResult<AssistantSessionsInsight>> GetSessionsAsync(DashboardScope scope, CancellationToken cancellationToken = default) => Asked(scope, Sessions);

        public Task<InsightResult<HoursWorkedInsight>> GetHoursWorkedAsync(DashboardScope scope, CancellationToken cancellationToken = default) => Asked(scope, HoursWorked);

        public Task<InsightResult<DriftInsight>> GetDriftAsync(DashboardScope scope, CancellationToken cancellationToken = default) => Asked(scope, Drift);

        public Task<NeedsYouInsight> GetNeedsYouAsync(DashboardScope scope, CancellationToken cancellationToken = default) => Asked(scope, NeedsYou);

        public void Invalidate()
        {
        }

        public Task<InsightResult<SpendThisMonthInsight>> GetThisMonthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendThisMonthInsight>.Ready(SpendThisMonthInsight.Empty));

        public Task<InsightResult<SpendByModelInsight>> GetByModelAsync(CancellationToken cancellationToken = default) => Task.FromResult(ByModel);

        public Task<InsightResult<SpendProjectionInsight>> GetProjectionAsync(CancellationToken cancellationToken = default) => Task.FromResult(Projection);

        public Task<InsightResult<SpendByDayInsight>> GetByDayAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByDayInsight>.Ready(SpendByDayInsight.Empty));

        public Task<InsightResult<SpendByWeekInsight>> GetByWeekAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByWeekInsight>.Ready(new SpendByWeekInsight([], "USD", false)));

        private Task<T> Asked<T>(DashboardScope scope, T answer)
        {
            Scopes.Add(scope);
            return Task.FromResult(answer);
        }

        private static PlanItemProgress Item(string title, int total, int done) =>
            new(Guid.NewGuid(), title, Today.AddDays(-14), Today.AddDays(7), [], 4, 1, total, done, 0, false, null, [], false);

        private static DriftUnit Unit(string unit, string verdict) =>
            new("backlog", unit, "chapter", "pull", null, verdict, null, null, Now);
    }
}
