using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI;
using Backlog.SharedKernel.Ai;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Dashboard's Ask AI content: the pull requests and issues behind the
/// figures, fetched for the scope the pane is showing.
/// </summary>
public class DashboardAiContentSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

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
        Assert.Equal(
            "Dashboard (12 weeks, every repository): 3 entries.\n"
            + "PR #412: Newer feature — merged, backlog, 2026-09-20, 2 review rounds, 120 lines in 4 files\n"
            + "---\n"
            + "PR #410: Older fix — merged, backlog, 2026-09-02, not reviewed\n"
            + "---\n"
            + "Issue #77: Chips reorder — closed, backlog, 2026-09-21",
            content.Body);
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

        Assert.StartsWith(
            "Dashboard (4 weeks, docs): 1 entry.\nOne or more repositories could not be read.\nPR #1: Only one",
            content.Body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_provider_the_body_says_so_and_nothing_is_fetched()
    {
        var activity = new StubActivity { Unavailable = "GitHub is not signed in." };

        var content = await Source(activity).ComposeAsync(new AiContentRequest("anything", 6000), TestContext.Current.CancellationToken);

        Assert.Equal("Dashboard: unavailable. GitHub is not signed in.", content.Body);
        Assert.Empty(activity.Fetches);
    }

    private static DashboardAiContentSource Source(StubActivity activity, DashboardScopeInView? scope = null) =>
        new(activity, new TwoRepositories(), scope ?? new DashboardScopeInView(), new FakeTimeProvider(Now));

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
}
