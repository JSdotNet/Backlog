using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.Services;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The four productivity parts, and what they say when the source cannot answer.
/// </summary>
public class ProductivityInsightsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_unavailable_source_gives_every_part_the_sources_own_words()
    {
        var insights = Insights(new StubActivitySource
        {
            Availability = InsightAvailability.Unavailable("Sign in with `gh auth login`.")
        });

        var headline = await insights.GetHeadlineAsync(DashboardScope.Default);
        var score = await insights.GetScoreAsync(DashboardScope.Default);
        var trend = await insights.GetTrendAsync(DashboardScope.Default);
        var rework = await insights.GetReworkAsync(DashboardScope.Default);

        foreach (var reason in new[]
                 {
                     headline.Availability.Reason,
                     score.Availability.Reason,
                     trend.Availability.Reason,
                     rework.Availability.Reason
                 })
        {
            Assert.Equal("Sign in with `gh auth login`.", reason);
        }

        Assert.False(headline.HasValue);
    }

    /// <summary>
    /// A provider that throws must not take the part down. The module turns it into
    /// the same unavailable-with-a-reason shape as a refusal, because the reader
    /// needs a sentence either way and a surface that renders six parts and an
    /// exception is worse than one that renders six parts and an explanation.
    /// </summary>
    [Fact]
    public async Task A_source_that_throws_becomes_a_reason_rather_than_an_exception()
    {
        var insights = Insights(new StubActivitySource
        {
            Throw = new InvalidOperationException("GitHub answered 502.")
        });

        var headline = await insights.GetHeadlineAsync(DashboardScope.Default);

        Assert.False(headline.HasValue);
        Assert.Equal("GitHub answered 502.", headline.Availability.Reason);
    }

    /// <summary>
    /// Cancellation is the reader closing the dashboard or changing the filter, not
    /// a source failing, so it travels instead of being reported as unavailable.
    /// </summary>
    [Fact]
    public async Task Cancelling_a_fetch_is_not_reported_as_an_unavailable_source()
    {
        var insights = Insights(new StubActivitySource
        {
            Throw = new OperationCanceledException()
        });

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => insights.GetHeadlineAsync(DashboardScope.Default));
    }

    [Fact]
    public async Task Four_parts_share_one_fetch_rather_than_making_four()
    {
        var source = new StubActivitySource();
        var insights = Insights(source);

        _ = await insights.GetHeadlineAsync(DashboardScope.Default);
        _ = await insights.GetScoreAsync(DashboardScope.Default);
        _ = await insights.GetTrendAsync(DashboardScope.Default);
        _ = await insights.GetReworkAsync(DashboardScope.Default);

        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Refreshing_goes_back_to_the_provider()
    {
        var source = new StubActivitySource();
        var insights = Insights(source);

        _ = await insights.GetHeadlineAsync(DashboardScope.Default);
        insights.Invalidate(DashboardScope.Default);
        _ = await insights.GetHeadlineAsync(DashboardScope.Default);

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Focusing_a_repository_narrows_the_fetch_to_that_one()
    {
        var source = new StubActivitySource();
        var insights = Insights(source);

        _ = await insights.GetHeadlineAsync(new DashboardScope("backlog-ide"));

        var asked = Assert.Single(source.Requested);
        Assert.Equal("backlog-ide", asked.Alias);
    }

    /// <summary>
    /// A filter that fails open is worse than one that shows an empty part: if the
    /// alias has gone from Settings, the answer is nothing rather than everything.
    /// </summary>
    [Fact]
    public async Task An_alias_that_no_longer_exists_narrows_to_nothing_rather_than_everything()
    {
        var source = new StubActivitySource();
        var insights = Insights(source);

        _ = await insights.GetHeadlineAsync(new DashboardScope("deleted-repo"));

        Assert.Empty(source.Requested);
    }

    [Fact]
    public async Task The_rework_rate_counts_only_pull_requests_that_were_reviewed()
    {
        // Four merged: two reviewed, one of those churned, two never reviewed. The
        // rate is one in two, not one in four — an unreviewed merge is not evidence
        // of clean work.
        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [
                    Merged(1, reviewed: true, churned: true),
                    Merged(2, reviewed: true, churned: false),
                    Merged(3, reviewed: false, churned: false),
                    Merged(4, reviewed: false, churned: false)
                ],
                [])
        };

        var headline = await Insights(source).GetHeadlineAsync(DashboardScope.Default);
        var rework = await Insights(source).GetReworkAsync(DashboardScope.Default);

        Assert.True(headline.HasValue);
        Assert.Equal(0.5m, headline.Value!.ReworkRate);
        Assert.Equal(4, headline.Value.PullRequestsMerged);

        Assert.True(rework.HasValue);
        Assert.Equal(2, rework.Value!.PullRequestsReviewed);
        Assert.Equal(1, rework.Value.PullRequestsWithChurn);
        Assert.Equal(0.5m, rework.Value.Rate);
    }

    [Fact]
    public async Task A_capped_churn_figure_is_reported_as_capped()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [
                    Merged(1, reviewed: true, churned: true) with { ChurnComplete = false },
                    Merged(2, reviewed: true, churned: true)
                ],
                [])
        };

        var rework = await Insights(source).GetReworkAsync(DashboardScope.Default);

        Assert.True(rework.HasValue);
        Assert.False(rework.Value!.ChurnComplete);
    }

    [Fact]
    public async Task The_median_review_turnaround_is_not_dragged_by_one_pull_request_that_sat_over_a_holiday()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [
                    Turnaround(1, TimeSpan.FromHours(2)),
                    Turnaround(2, TimeSpan.FromHours(3)),
                    Turnaround(3, TimeSpan.FromDays(21))
                ],
                [])
        };

        var headline = await Insights(source).GetHeadlineAsync(DashboardScope.Default);

        Assert.True(headline.HasValue);
        Assert.Equal(TimeSpan.FromHours(3), headline.Value!.MedianReviewTurnaround);
    }

    [Fact]
    public async Task A_repository_that_reported_nothing_is_left_off_the_trend_rather_than_drawn_as_a_flat_zero()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport([Merged(1, reviewed: true, churned: false)], [])
        };

        var trend = await Insights(source).GetTrendAsync(DashboardScope.Default);

        Assert.True(trend.HasValue);
        var series = Assert.Single(trend.Value!.ByRepository);
        Assert.Equal("backlog", series.Name);
    }

    /// <summary>
    /// Zooming in moves a highlight rather than dropping the pack. The comparison
    /// components show one series against the others, and one line alone says
    /// nothing about whether a dip was this repository or a quiet fortnight
    /// everywhere.
    /// </summary>
    [Fact]
    public async Task Focusing_a_repository_still_returns_every_repository_that_reported()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [
                    Merged(1, reviewed: true, churned: false),
                    Merged(2, reviewed: true, churned: false) with { RepositoryAlias = "backlog-ide" }
                ],
                [])
        };

        var trend = await Insights(source).GetTrendAsync(DashboardScope.Default);

        Assert.True(trend.HasValue);
        Assert.Equal(2, trend.Value!.ByRepository.Count);
    }

    private static ProductivityInsights Insights(IActivitySource source) =>
        new(source, new StubRepositoryDirectory(), new FixedClock(Now));

    private static ActivityPullRequest Merged(int number, bool reviewed, bool churned)
    {
        var mergedAt = Now.AddDays(-number);

        return new ActivityPullRequest(
            "backlog",
            number,
            mergedAt,
            reviewed ? mergedAt.AddHours(-6) : null,
            ReviewRounds: reviewed ? 1 : 0,
            CommitsAfterFirstReview: churned ? 3 : 0,
            ForcePushesAfterFirstReview: churned ? 1 : 0,
            FilesRetouched: churned ? 2 : 0,
            ChurnComplete: true)
        {
            ReviewTurnaround = reviewed ? TimeSpan.FromHours(6) : null
        };
    }

    private static ActivityPullRequest Turnaround(int number, TimeSpan turnaround) =>
        Merged(number, reviewed: true, churned: false) with { ReviewTurnaround = turnaround };

    private sealed class StubRepositoryDirectory : IRepositoryDirectory
    {
        public IReadOnlyList<DashboardRepository> Repositories { get; } =
        [
            new("backlog", "JSdotNet/Backlog"),
            new("backlog-ide", "JSdotNet/Backlog.Ide")
        ];
    }

    private sealed class StubActivitySource : IActivitySource
    {
        public InsightAvailability Availability { get; init; } = InsightAvailability.Available;

        public ActivityReport Report { get; init; } = ActivityReport.Empty;

        public Exception? Throw { get; init; }

        public int Calls { get; private set; }

        public List<DashboardRepository> Requested { get; } = [];

        public Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Throw is not null ? Task.FromException<InsightAvailability>(Throw) : Task.FromResult(Availability);

        public Task<ActivityReport> GetActivityAsync(
            IReadOnlyList<DashboardRepository> repositories,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Requested.AddRange(repositories);
            return Task.FromResult(Report);
        }
    }

    /// <summary>A clock that does not move, so a window is the same window on every
    /// machine and on every run.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
