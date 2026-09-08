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

        var headline = await insights.GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        var score = await insights.GetScoreAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        var trend = await insights.GetTrendAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        var rework = await insights.GetReworkAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

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

        var headline = await insights.GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

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
            () => insights.GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Four_parts_share_one_fetch_rather_than_making_four()
    {
        var source = new StubActivitySource();
        var insights = Insights(source);

        _ = await insights.GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        _ = await insights.GetScoreAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        _ = await insights.GetTrendAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        _ = await insights.GetReworkAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.Equal(1, source.Calls);
    }

    /// <summary>
    /// GitHub cannot say which machine a pull request was worked from, so focusing one
    /// changes nothing about the call. The cache key leaves the machine out on purpose,
    /// and this is what holds it there: with the machine in the key, moving that filter
    /// would re-spend a quarter's churn budget to produce the identical answer.
    /// </summary>
    [Fact]
    public async Task Focusing_a_machine_does_not_send_the_activity_fetch_out_again()
    {
        var source = new StubActivitySource();
        var insights = Insights(source);

        _ = await insights.GetHeadlineAsync(DashboardScope.Default);
        _ = await insights.GetHeadlineAsync(DashboardScope.Default with { MachineId = "tower" });

        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Refreshing_goes_back_to_the_provider()
    {
        var source = new StubActivitySource();
        var insights = Insights(source);

        _ = await insights.GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        insights.Invalidate(DashboardScope.Default);
        _ = await insights.GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Focusing_a_repository_narrows_the_fetch_to_that_one()
    {
        var source = new StubActivitySource();
        var insights = Insights(source);

        _ = await insights.GetHeadlineAsync(new DashboardScope("backlog-ide"), TestContext.Current.CancellationToken);

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

        _ = await insights.GetHeadlineAsync(new DashboardScope("deleted-repo"), TestContext.Current.CancellationToken);

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

        var headline = await Insights(source).GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        var rework = await Insights(source).GetReworkAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

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

        var rework = await Insights(source).GetReworkAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

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

        var headline = await Insights(source).GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

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

        var trend = await Insights(source).GetTrendAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

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

        var trend = await Insights(source).GetTrendAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.True(trend.HasValue);
        Assert.Equal(2, trend.Value!.ByRepository.Count);
    }

    /// <summary>
    /// The baseline is a second provider call, and one per part per filter move would
    /// be as wasteful as the activity fetch it sits beside. One per focus, and the
    /// second reader of the same focus joins it.
    /// </summary>
    [Fact]
    public async Task The_baseline_is_fetched_once_per_focus_and_cached()
    {
        var baseline = new StubBaselineSource();
        var insights = Insights(new StubActivitySource(), baseline);

        _ = await insights.GetScoreAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        _ = await insights.GetScoreAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.Equal(1, baseline.Calls);

        // A different focus is a different question — a repository's own record is
        // not the estate's — so it does go out again.
        _ = await insights.GetScoreAsync(new DashboardScope("backlog-ide"), TestContext.Current.CancellationToken);

        Assert.Equal(2, baseline.Calls);
    }

    /// <summary>
    /// Both cached answers live in one dictionary and <c>GetOrAddAsync</c> casts what
    /// it finds to the type the caller asked for, so a baseline stored under an
    /// activity key would not read as a miss — it would throw at the cast and take
    /// the part down. The prefix is what keeps them apart, and this is what holds it
    /// there.
    /// </summary>
    [Fact]
    public async Task The_baseline_cache_key_does_not_collide_with_the_activity_key()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport([Merged(1, reviewed: true, churned: false)], [])
        };

        var baseline = new StubBaselineSource();
        var insights = Insights(source, baseline);

        var headline = await insights.GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        var score = await insights.GetScoreAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.True(headline.HasValue);
        Assert.True(score.HasValue);

        // One of each, rather than one of them evicting the other and being re-asked.
        Assert.Equal(1, source.Calls);
        Assert.Equal(1, baseline.Calls);

        // And the baseline is the one that landed under the baseline key.
        Assert.NotNull(score.Value!.Target);
    }

    /// <summary>
    /// A baseline is only able to take the three volume inputs off the card. Turning
    /// the whole part unavailable for it would trade four inputs a reader could still
    /// use for a sentence they cannot.
    /// </summary>
    [Fact]
    public async Task A_baseline_that_refuses_leaves_the_score_readable()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [Merged(1, reviewed: true, churned: false), Merged(2, reviewed: true, churned: true)],
                [])
        };

        var baseline = new StubBaselineSource { Throw = new InvalidOperationException("GitHub answered 502.") };

        var score = await Insights(source, baseline)
            .GetScoreAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.True(score.HasValue);
        Assert.Null(score.Value!.Target);

        // The volume inputs are gone, rather than scored against a target of nothing.
        Assert.DoesNotContain(score.Value.Inputs, input => input.Label == "Pull requests merged");

        // And what could still be judged still is.
        Assert.Contains(score.Value.Inputs, input => input.Label == "First review within a day");
    }

    /// <summary>
    /// A listing that stopped early makes every count on the surface a floor, and the
    /// score is built on counts. The flag has to survive the whole derivation or the
    /// part cannot say so.
    /// </summary>
    [Fact]
    public async Task A_truncated_listing_travels_all_the_way_to_the_score()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport([Merged(1, reviewed: true, churned: false)], []) { Complete = false }
        };

        var insights = Insights(source);

        var score = await insights.GetScoreAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        var headline = await insights.GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        var rework = await insights.GetReworkAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        var trend = await insights.GetTrendAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.False(score.Value!.Complete);
        Assert.False(headline.Value!.Complete);
        Assert.False(rework.Value!.Complete);
        Assert.False(trend.Value!.Complete);
    }

    /// <summary>
    /// <c>ScorePart.FollowsMachine</c> is false, and this is what makes that literally
    /// true rather than merely undisplayed: the sessions figure behind the score is
    /// read across every machine whatever the filter above it says.
    /// </summary>
    [Fact]
    public async Task The_score_reads_every_machine_even_under_a_machine_focus()
    {
        var sessions = new StubSessionInsights { Sessions = 40, PerWeek = PerWeek() };

        _ = await Insights(new StubActivitySource(), sessions: sessions)
            .GetScoreAsync(
                DashboardScope.Default with { MachineId = "tower" },
                TestContext.Current.CancellationToken);

        Assert.NotEmpty(sessions.Scopes);
        Assert.All(sessions.Scopes, scope => Assert.Null(scope.MachineId));
    }

    /// <summary>
    /// The sessions figure is the reader's own best four weeks of sessions, on the
    /// same footing as the two GitHub volumes — one a week is a target for somebody
    /// who runs one a week and an insult to somebody who runs forty.
    /// </summary>
    [Fact]
    public async Task The_sessions_input_is_scored_against_the_readers_own_best_four_weeks()
    {
        var sessions = new StubSessionInsights { Sessions = 40, PerWeek = PerWeek() };

        var score = await Insights(new StubActivitySource(), sessions: sessions)
            .GetScoreAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        var input = Assert.Single(score.Value!.Inputs, one => one.Label == "Assistant sessions");

        // The best four weeks hold twenty, which is five a week; over a quarter, a
        // quarter above that is seventy-five.
        Assert.Equal(40m, input.Value);
        Assert.Equal(75m, input.Max);
    }

    /// <summary>
    /// No assistant records which repository a session was for, so a focused reader
    /// gets no sessions input at all — the source is not even asked, because there is
    /// no answer it could give that would belong to one repository.
    /// </summary>
    [Fact]
    public async Task Sessions_are_left_out_of_the_score_when_one_repository_is_in_focus()
    {
        var sessions = new StubSessionInsights { Sessions = 40, PerWeek = PerWeek() };

        var score = await Insights(new StubActivitySource(), sessions: sessions)
            .GetScoreAsync(new DashboardScope("backlog-ide"), TestContext.Current.CancellationToken);

        Assert.Empty(sessions.Scopes);
        Assert.DoesNotContain(score.Value!.Inputs, input => input.Label == "Assistant sessions");
    }

    /// <summary>
    /// A source that refuses omits the input rather than failing the score, and
    /// rather than scoring the reader as having run nothing.
    /// </summary>
    [Fact]
    public async Task A_session_source_that_refuses_leaves_the_input_out_rather_than_scoring_a_zero()
    {
        var sessions = new StubSessionInsights { Refusal = "No assistant folder on this machine." };

        var score = await Insights(new StubActivitySource(), sessions: sessions)
            .GetScoreAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.True(score.HasValue);
        Assert.DoesNotContain(score.Value!.Inputs, input => input.Label == "Assistant sessions");
    }

    /// <summary>
    /// Zooming in moves a highlight rather than dropping the pack, and the trend has
    /// to read the UNFOCUSED window for that to be possible at all. Its own remark
    /// and <c>ProductivityTrend</c>'s doc comment both said so while the code
    /// narrowed to <c>scoped.Repositories</c>; the covering test used the default
    /// scope and could not see it.
    /// </summary>
    [Fact]
    public async Task Every_repository_stays_in_the_trend_when_one_is_in_focus()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [
                    Merged(1, reviewed: true, churned: false),
                    Merged(2, reviewed: true, churned: false) with { RepositoryAlias = "backlog-ide" },
                    Merged(3, reviewed: true, churned: false) with { RepositoryAlias = "backlog-mobile" }
                ],
                [])
        };

        var trend = await Insights(source)
            .GetTrendAsync(new DashboardScope("backlog-ide"), TestContext.Current.CancellationToken);

        Assert.True(trend.HasValue);
        Assert.Equal(3, trend.Value!.ByRepository.Count);
        Assert.Equal("backlog-ide", trend.Value.Highlight);
    }

    /// <summary>
    /// One target across the whole trellis, taken from the estate's record. Per-series
    /// normalisation would make every repository's best week read 100 — the busiest
    /// and the quietest alike — and destroy the only comparison the spotlight exists
    /// to draw.
    /// </summary>
    [Fact]
    public async Task Every_repositorys_week_is_judged_against_the_same_target()
    {
        var week = Now.AddDays(-1);

        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [
                    .. Enumerable.Range(1, 8).Select(number =>
                        Merged(number, reviewed: false, churned: false) with { MergedAt = week }),
                    .. Enumerable.Range(9, 2).Select(number =>
                        Merged(number, reviewed: false, churned: false) with
                        {
                            RepositoryAlias = "backlog-ide",
                            MergedAt = week
                        })
                ],
                [])
        };

        // Forty merges in the best block is ten a week, so one week's full marks is
        // twelve and a half.
        var baseline = new StubBaselineSource { MergedPerBlock = 40, ClosedPerBlock = 0 };

        var trend = await Insights(source, baseline)
            .GetTrendAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        var busy = Assert.Single(trend.Value!.ByRepository, series => series.Name == "backlog");
        var quiet = Assert.Single(trend.Value.ByRepository, series => series.Name == "backlog-ide");

        Assert.Equal(64m, busy.Points.Max(point => point.Value));
        Assert.Equal(16m, quiet.Points.Max(point => point.Value));

        // Neither reads full marks, which is what per-series normalisation would have
        // given both of them.
        Assert.All(
            trend.Value.ByRepository,
            series => Assert.All(series.Points, point => Assert.True(point.Value < 100m)));
    }

    /// <summary>Twelve weeks of sessions whose busiest four hold twenty.</summary>
    private static IReadOnlyList<InsightPoint> PerWeek() =>
    [
        .. Enumerable.Range(0, 12).Select(index =>
            new InsightPoint("W" + index, index < 4 ? 1m : index < 8 ? 2m : 5m))
    ];

    /// <summary>
    /// The whole derivation over doubles. Every collaborator has a default that
    /// answers nothing rather than refusing, so a test names only the one it is
    /// about — a baseline test does not have to describe a session source, and none
    /// of the tests that predate either of them had to learn about them.
    /// </summary>
    private static ProductivityInsights Insights(
        IActivitySource source,
        IActivityBaselineSource? baseline = null,
        ISessionInsights? sessions = null,
        IRepositoryDirectory? repositories = null) =>
        new(
            source,
            baseline ?? new StubBaselineSource(),
            repositories ?? new StubRepositoryDirectory(),
            sessions ?? new StubSessionInsights(),
            new FixedClock(Now));

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
            new("backlog-ide", "JSdotNet/Backlog.Ide"),
            new("backlog-mobile", "JSdotNet/Backlog.Mobile")
        ];
    }

    /// <summary>
    /// The reader's own record. Answers the same counts for every block by default,
    /// so a test that does not care about the shape of the history still gets a
    /// target and the volume inputs still appear.
    /// </summary>
    private sealed class StubBaselineSource : IActivityBaselineSource
    {
        public int MergedPerBlock { get; init; } = 40;

        public int ClosedPerBlock { get; init; } = 20;

        public bool Complete { get; init; } = true;

        public Exception? Throw { get; init; }

        public int Calls { get; private set; }

        public List<DashboardRepository> Requested { get; } = [];

        public List<ActivityWindow> Blocks { get; } = [];

        public Task<ActivityBaseline> GetBaselineAsync(
            IReadOnlyList<DashboardRepository> repositories,
            IReadOnlyList<ActivityWindow> blocks,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Requested.AddRange(repositories);
            Blocks.AddRange(blocks);

            if (Throw is not null) return Task.FromException<ActivityBaseline>(Throw);

            return Task.FromResult(new ActivityBaseline(
                [.. blocks.Select(block => new ActivityVolume(
                    block.From,
                    block.To,
                    MergedPerBlock,
                    ClosedPerBlock))],
                Complete));
        }
    }

    /// <summary>
    /// Sessions, as the score sees them. Records the scopes it was asked for, which
    /// is how the machine-blanking claim is asserted rather than described.
    /// </summary>
    private sealed class StubSessionInsights : ISessionInsights
    {
        public int Sessions { get; init; }

        public IReadOnlyList<InsightPoint> PerWeek { get; init; } = [];

        public string? Refusal { get; init; }

        public List<DashboardScope> Scopes { get; } = [];

        public Task<InsightResult<AssistantSessionsInsight>> GetSessionsAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);

            return Task.FromResult(Refusal is not null
                ? InsightResult<AssistantSessionsInsight>.Unavailable(Refusal)
                : InsightResult<AssistantSessionsInsight>.Ready(
                    AssistantSessionsInsight.Empty with { Sessions = Sessions, SessionsPerWeek = PerWeek }));
        }

        public void Invalidate()
        {
        }
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
