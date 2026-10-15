using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.Services;
using Microsoft.Extensions.Time.Testing;

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
        var rework = await insights.GetReworkAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        foreach (var reason in new[]
                 {
                     headline.Availability.Reason,
                     score.Availability.Reason,
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
    public async Task Three_parts_share_one_fetch_rather_than_making_three()
    {
        var source = new StubActivitySource();
        var insights = Insights(source);

        _ = await insights.GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        _ = await insights.GetScoreAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
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

        _ = await insights.GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        _ = await insights.GetHeadlineAsync(DashboardScope.Default with { MachineId = "tower" }, TestContext.Current.CancellationToken);

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

        _ = await insights.GetHeadlineAsync(new DashboardScope(RepositoryFocus.Of("backlog-ide")), TestContext.Current.CancellationToken);

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

        _ = await insights.GetHeadlineAsync(new DashboardScope(RepositoryFocus.Of("deleted-repo")), TestContext.Current.CancellationToken);

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
        Assert.Equal(2, headline.Value.PullRequestsReviewed);

        Assert.True(rework.HasValue);
        Assert.Equal(2, rework.Value!.PullRequestsReviewed);
        Assert.Equal(1, rework.Value.PullRequestsWithChurn);
        Assert.Equal(0.5m, rework.Value.Rate);
    }

    /// <summary>
    /// Merges with no review at all: the rate is a share of nothing, and the headline says
    /// how many were reviewed - none - so a surface can print "—" where verify read "0%
    /// came back" beside a First review of "—".
    /// </summary>
    [Fact]
    public async Task A_window_with_no_reviewed_merge_says_none_were_reviewed()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [
                    Merged(1, reviewed: false, churned: false),
                    Merged(2, reviewed: false, churned: false)
                ],
                [])
        };

        var headline = await Insights(source).GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.True(headline.HasValue);
        Assert.Equal(0, headline.Value!.PullRequestsReviewed);
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
        _ = await insights.GetScoreAsync(new DashboardScope(RepositoryFocus.Of("backlog-ide")), TestContext.Current.CancellationToken);

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
        // The baseline is asked twice because it answers two questions — the record
        // the score is read against, and the window before this one the headline is
        // compared with — each under a key of its own.
        Assert.Equal(1, source.Calls);
        Assert.Equal(2, baseline.Calls);

        // And the baseline is the one that landed under the baseline key.
        Assert.NotNull(score.Value!.Target);
        Assert.NotNull(headline.Value!.PreviousPullRequestsMerged);
    }

    /// <summary>
    /// The window before this one is a count and nothing else, so it is one block of the
    /// baseline port — the cheap one — of exactly the window's length, ending where the
    /// window opens. Midnight on 19 August less four weeks is 22 July, and four weeks
    /// before that is 24 June.
    /// </summary>
    [Fact]
    public async Task The_previous_window_is_one_baseline_block_of_the_same_length_ending_where_the_window_opens()
    {
        var baseline = new StubBaselineSource { MergedPerBlock = 17 };

        var headline = await Insights(new StubActivitySource(), baseline)
            .GetHeadlineAsync(new DashboardScope(Period: DashboardPeriod.FourWeeks), TestContext.Current.CancellationToken);

        Assert.True(headline.HasValue);
        Assert.Equal(17, headline.Value!.PreviousPullRequestsMerged);

        var block = Assert.Single(baseline.Blocks);
        Assert.Equal(new DateTimeOffset(2026, 6, 24, 0, 0, 0, TimeSpan.Zero), block.From);
        Assert.Equal(new DateTimeOffset(2026, 7, 22, 0, 0, 0, TimeSpan.Zero), block.To);
    }

    [Fact]
    public async Task Twelve_weeks_is_compared_with_the_twelve_before_it()
    {
        var baseline = new StubBaselineSource();

        _ = await Insights(new StubActivitySource(), baseline)
            .GetHeadlineAsync(new DashboardScope(Period: DashboardPeriod.TwelveWeeks), TestContext.Current.CancellationToken);

        var block = Assert.Single(baseline.Blocks);
        Assert.Equal(new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero), block.From);
        Assert.Equal(new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.Zero), block.To);
    }

    /// <summary>
    /// The comparison is decoration on a figure that stands without it, so a baseline
    /// that refuses takes the comparison off the tile and leaves the tile.
    /// </summary>
    [Fact]
    public async Task A_baseline_that_throws_leaves_the_headline_without_a_previous_figure()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport([Merged(1, reviewed: true, churned: false)], [])
        };

        var headline = await Insights(source, new StubBaselineSource { Throw = new InvalidOperationException("GitHub answered 502.") })
            .GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.True(headline.HasValue);
        Assert.Equal(1, headline.Value!.PullRequestsMerged);
        Assert.Null(headline.Value.PreviousPullRequestsMerged);
    }

    /// <summary>
    /// An answer with no block in it — nothing configured to ask — and an answer that
    /// says one of its blocks could not be counted are both null rather than a number. A
    /// floor set beside a whole figure would draw growth that never happened.
    /// </summary>
    [Fact]
    public async Task A_baseline_with_no_block_or_an_incomplete_one_gives_no_previous_figure()
    {
        var empty = await Insights(new StubActivitySource(), new StubBaselineSource { Answer = ActivityBaseline.Empty })
            .GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        var partial = await Insights(new StubActivitySource(), new StubBaselineSource { Complete = false })
            .GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.True(empty.HasValue);
        Assert.Null(empty.Value!.PreviousPullRequestsMerged);
        Assert.True(partial.HasValue);
        Assert.Null(partial.Value!.PreviousPullRequestsMerged);
    }

    /// <summary>
    /// A listing that stopped early makes the merged count a floor, and a floor set
    /// beside a whole earlier count would draw a fall that never happened — so the
    /// comparison goes, and the count stays with the note that already says it is one.
    /// </summary>
    [Fact]
    public async Task A_truncated_window_gives_no_previous_figure()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport([Merged(1, reviewed: true, churned: false)], []) { Complete = false }
        };

        var headline = await Insights(source, new StubBaselineSource())
            .GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.True(headline.HasValue);
        Assert.False(headline.Value!.Complete);
        Assert.Equal(1, headline.Value.PullRequestsMerged);
        Assert.Null(headline.Value.PreviousPullRequestsMerged);
    }

    [Fact]
    public async Task The_previous_window_is_asked_once_per_focus_and_narrowed_to_it()
    {
        var baseline = new StubBaselineSource();
        var insights = Insights(new StubActivitySource(), baseline);
        var focused = new DashboardScope(RepositoryFocus.Of("backlog-ide"));

        _ = await insights.GetHeadlineAsync(focused, TestContext.Current.CancellationToken);
        _ = await insights.GetHeadlineAsync(focused, TestContext.Current.CancellationToken);

        Assert.Equal(1, baseline.Calls);
        Assert.Equal(["backlog-ide"], baseline.Requested.Select(repository => repository.Alias));
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

        // The volume card is empty, rather than scored against a target of nothing.
        Assert.Empty(score.Value.Volume.Inputs);

        // And what could still be judged still is — on the other card.
        Assert.Contains(score.Value.Quality.Inputs, input => input.Label == "First review within a day");
        Assert.True(score.Value.HasInputs);
    }

    /// <summary>
    /// The split reaches the port: the volume card carries the two counts, the
    /// quality card the four proportions, and no session figure is fetched or scored
    /// for either. The insights service no longer takes a session port at all, which
    /// the constructor asserts more firmly than any test could.
    /// </summary>
    [Fact]
    public async Task The_score_is_two_cards_and_neither_counts_sessions()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [Merged(1, reviewed: true, churned: false), Merged(2, reviewed: true, churned: true)],
                [Closed(1)])
        };

        var score = await Insights(source, new StubBaselineSource { MergedPerBlock = 8, ClosedPerBlock = 4 })
            .GetScoreAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.True(score.HasValue);
        Assert.Equal(["Pull requests merged", "Issues closed"], score.Value!.Volume.Inputs.Select(input => input.Label));
        Assert.Equal(
            ["First review within a day", "Merged without post-review churn"],
            score.Value.Quality.Inputs.Select(input => input.Label));

        Assert.DoesNotContain(
            score.Value.Volume.Inputs.Concat(score.Value.Quality.Inputs),
            input => input.Label.Contains("session", StringComparison.OrdinalIgnoreCase));

        // Each card's figure is its own composition's, not the other's.
        Assert.Equal(ProductivityScoring.Score(score.Value.Volume.Inputs), score.Value.Volume.Value);
        Assert.Equal(ProductivityScoring.Score(score.Value.Quality.Inputs), score.Value.Quality.Value);
        Assert.NotEqual(score.Value.Volume.Value, score.Value.Quality.Value);
    }

    /// <summary>
    /// The commit count comes off the same detail call as the size, so a pull request
    /// whose detail was not read has no count rather than a count of zero — and is
    /// left out of the median instead of dragging it to the floor.
    /// </summary>
    [Fact]
    public async Task The_median_commit_count_is_over_pull_requests_whose_detail_was_read()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [
                    Merged(1, reviewed: false, churned: false) with { SizeKnown = true, Commits = 9 },
                    Merged(2, reviewed: false, churned: false) with { SizeKnown = true, Commits = 2 },
                    Merged(3, reviewed: false, churned: false) with { SizeKnown = true, Commits = 4 },
                    Merged(4, reviewed: false, churned: false) with { SizeKnown = true, Commits = 30 },
                    Merged(5, reviewed: false, churned: false) with { SizeKnown = false, Commits = 0 }
                ],
                [])
        };

        var headline = await Insights(source).GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        // Sorted, 2 4 9 30: the lower middle is 4, and the unread fifth is not a 0
        // in front of them.
        Assert.Equal(4, headline.Value!.MedianCommitsPerPullRequest);
        Assert.Equal(4, headline.Value.PullRequestsWithCommitCount);
        Assert.Equal(5, headline.Value.PullRequestsMerged);
    }

    [Fact]
    public async Task No_readable_detail_gives_no_median_rather_than_a_zero_one()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport([Merged(1, reviewed: false, churned: false)], [])
        };

        var headline = await Insights(source).GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.Null(headline.Value!.MedianCommitsPerPullRequest);
        Assert.Equal(0, headline.Value.PullRequestsWithCommitCount);
    }

    /// <summary>
    /// The weekly series behind the commits measure and the two tiles that had no
    /// trend: commits summed by the week a pull request merged, and the two medians
    /// cut by the same weeks. A pull request whose detail was not read adds no
    /// commits and is not in either median, and a week with nothing to take a median
    /// over has no point rather than a zero one (entry 19).
    /// </summary>
    [Fact]
    public async Task Commits_and_the_two_medians_are_cut_by_the_week_a_pull_request_merged()
    {
        // Now is Wednesday 19 August. Days 1 and 2 merged this ISO week (W34), day 3
        // on the Sunday before it (W33), and day 4 is the W33 merge whose detail
        // could not be read.
        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [
                    Turnaround(1, TimeSpan.FromHours(2)) with { SizeKnown = true, Commits = 3 },
                    Turnaround(2, TimeSpan.FromHours(8)) with { SizeKnown = true, Commits = 7 },
                    Merged(3, reviewed: false, churned: false) with { SizeKnown = true, Commits = 5 },
                    Turnaround(4, TimeSpan.FromHours(30)) with { SizeKnown = false, Commits = 0 }
                ],
                [])
        };

        var headline = (await Insights(source).GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken)).Value!;

        var commits = headline.CommitsPerWeek.ToDictionary(point => point.Label, point => point.Value);
        var turnaround = headline.ReviewTurnaroundPerWeek.ToDictionary(point => point.Label, point => point.Value);
        var perPullRequest = headline.CommitsPerPullRequestPerWeek.ToDictionary(point => point.Label, point => point.Value);

        Assert.Equal(headline.PullRequestsPerWeek.Select(point => point.Label), headline.CommitsPerWeek.Select(point => point.Label));
        Assert.Equal(10m, commits["W34"]);
        Assert.Equal(5m, commits["W33"]);

        // Lower middle of 2 h and 8 h; W33's one reviewed merge is the unread one, and
        // its turnaround still counts — turnaround does not come off the detail call.
        Assert.Equal(2m, turnaround["W34"]);
        Assert.Equal(30m, turnaround["W33"]);

        Assert.Equal(3m, perPullRequest["W34"]);
        Assert.Equal(5m, perPullRequest["W33"]);

        // Every other week of the window had no merge, so neither median has a point
        // there; the commits measure is a sum and keeps its zero.
        Assert.Equal(["W33", "W34"], headline.ReviewTurnaroundPerWeek.Select(point => point.Label));
        Assert.Equal(["W33", "W34"], headline.CommitsPerPullRequestPerWeek.Select(point => point.Label));
        Assert.Equal(headline.PullRequestsPerWeek.Count, headline.CommitsPerWeek.Count);
    }

    /// <summary>
    /// A week whose merges were none of them reviewed has no first-review wait to take
    /// a median of, and a week whose merges' detail was not read has no commit count:
    /// each is left out of its line rather than drawn as 0 h or 0 commits (entry 19).
    /// </summary>
    [Fact]
    public async Task A_week_with_no_evidence_has_no_median_point()
    {
        // Wednesday 19 August: day 1 merged this week (W34), unreviewed and with its
        // detail read; day 3 on the Sunday before (W33), reviewed, detail not read.
        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [
                    Merged(1, reviewed: false, churned: false) with { SizeKnown = true, Commits = 4 },
                    Turnaround(3, TimeSpan.FromHours(5)) with { SizeKnown = false, Commits = 0 }
                ],
                [])
        };

        var headline = (await Insights(source).GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken)).Value!;

        var turnaround = Assert.Single(headline.ReviewTurnaroundPerWeek);
        Assert.Equal("W33", turnaround.Label);
        Assert.Equal(5m, turnaround.Value);

        var perPullRequest = Assert.Single(headline.CommitsPerPullRequestPerWeek);
        Assert.Equal("W34", perPullRequest.Label);
        Assert.Equal(4m, perPullRequest.Value);
    }

    /// <summary>
    /// Review rounds and change requests are summed over the reviewed pull requests,
    /// on the same denominator as every other rework figure. An unreviewed merge has
    /// no rounds to add, and adding its zero would be counting it as reviewed.
    /// </summary>
    [Fact]
    public async Task Review_rounds_and_change_requests_are_summed_over_reviewed_pull_requests()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [
                    Merged(1, reviewed: true, churned: true) with { ReviewRounds = 3, ChangesRequested = 2 },
                    Merged(2, reviewed: true, churned: false) with { ReviewRounds = 1, ChangesRequested = 0 },
                    Merged(3, reviewed: false, churned: false)
                ],
                [])
        };

        var rework = await Insights(source).GetReworkAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.Equal(4, rework.Value!.ReviewRounds);
        Assert.Equal(2, rework.Value.ChangesRequested);
        Assert.Equal(2, rework.Value.PullRequestsReviewed);
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

        Assert.False(score.Value!.Complete);
        Assert.False(headline.Value!.Complete);
        Assert.False(rework.Value!.Complete);
    }

    /// <summary>
    /// Churn by repository ranks by the pull requests that came back after review, the
    /// figure the card is about, and each row carries its own reviewed count so the
    /// rows add up to the totals the part prints under them (entry 20).
    /// </summary>
    [Fact]
    public async Task Churn_by_repository_ranks_by_pull_requests_that_churned()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [
                    // backlog: one churned, but with many commits after review.
                    Merged(1, reviewed: true, churned: true) with { CommitsAfterFirstReview = 30 },
                    Merged(2, reviewed: true, churned: false),
                    // backlog-ide: two churned, few commits each.
                    Merged(3, reviewed: true, churned: true) with { RepositoryAlias = "backlog-ide", CommitsAfterFirstReview = 1 },
                    Merged(4, reviewed: true, churned: true) with { RepositoryAlias = "backlog-ide", CommitsAfterFirstReview = 1 },
                    Merged(5, reviewed: true, churned: false) with { RepositoryAlias = "backlog-ide" },
                    // backlog-mobile: reviewed, nothing came back.
                    Merged(6, reviewed: true, churned: false) with { RepositoryAlias = "backlog-mobile" },
                    // never reviewed: in no row.
                    Merged(7, reviewed: false, churned: false) with { RepositoryAlias = "backlog-mobile" }
                ],
                [])
        };

        var rework = (await Insights(source).GetReworkAsync(DashboardScope.Default, TestContext.Current.CancellationToken)).Value!;

        Assert.Equal(
            [("backlog-ide", 2, 3), ("backlog", 1, 2), ("backlog-mobile", 0, 1)],
            rework.ByRepository.Select(row => (row.Alias, row.PullRequestsWithChurn, row.PullRequestsReviewed)));
        Assert.Equal(rework.PullRequestsWithChurn, rework.ByRepository.Sum(row => row.PullRequestsWithChurn));
        Assert.Equal(rework.PullRequestsReviewed, rework.ByRepository.Sum(row => row.PullRequestsReviewed));
    }

    /// <summary>
    /// The baseline port counts any stretch of the past GitHub can search, so even the
    /// widest window keeps its comparison: 26 weeks back from midnight on 19 August is
    /// 18 February, and the 26 before that open on 20 August 2025 (AC2).
    /// </summary>
    [Fact]
    public async Task Twenty_six_weeks_is_compared_with_the_twenty_six_before_it()
    {
        var baseline = new StubBaselineSource { MergedPerBlock = 90 };

        var headline = await Insights(new StubActivitySource(), baseline)
            .GetHeadlineAsync(new DashboardScope(Period: DashboardPeriod.TwentySixWeeks), TestContext.Current.CancellationToken);

        Assert.Equal(90, headline.Value!.PreviousPullRequestsMerged);
        var block = Assert.Single(baseline.Blocks);
        Assert.Equal(new DateTimeOffset(2025, 8, 20, 0, 0, 0, TimeSpan.Zero), block.From);
        Assert.Equal(new DateTimeOffset(2026, 2, 18, 0, 0, 0, TimeSpan.Zero), block.To);
    }

    [Fact]
    public async Task Eight_weeks_is_compared_with_the_eight_before_it()
    {
        var baseline = new StubBaselineSource();

        _ = await Insights(new StubActivitySource(), baseline)
            .GetHeadlineAsync(new DashboardScope(Period: DashboardPeriod.EightWeeks), TestContext.Current.CancellationToken);

        var block = Assert.Single(baseline.Blocks);
        Assert.Equal(new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero), block.From);
        Assert.Equal(new DateTimeOffset(2026, 6, 24, 0, 0, 0, TimeSpan.Zero), block.To);
    }

    /// <summary>
    /// The whole derivation over doubles. Every collaborator has a default that
    /// answers nothing rather than refusing, so a test names only the one it is
    /// about — a rework test does not have to describe a baseline, and none of the
    /// tests that predate it had to learn about it.
    /// </summary>
    private static ProductivityInsights Insights(
        IActivitySource source,
        IActivityBaselineSource? baseline = null,
        IRepositoryDirectory? repositories = null) =>
        new(
            source,
            baseline ?? new StubBaselineSource(),
            repositories ?? new StubRepositoryDirectory(),
            new FakeTimeProvider(Now));

    /// <summary>
    /// The sync figures share the rework answer but not its denominator: every pull
    /// request whose branch synced counts, reviewed or not, and one whose commits
    /// could not be read counts for nothing rather than as a clean sync.
    /// </summary>
    [Fact]
    public async Task Rework_counts_conflicted_syncs_over_the_pull_requests_that_synced()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [
                    Merged(1, reviewed: true, churned: false) with { SyncMerges = 2, ConflictedSyncMerges = 1, SyncsKnown = true },
                    Merged(2, reviewed: false, churned: false) with { SyncMerges = 1, ConflictedSyncMerges = 1, SyncsKnown = true },
                    Merged(3, reviewed: false, churned: false) with { SyncMerges = 3, ConflictedSyncMerges = 0, SyncsKnown = true },
                    Merged(4, reviewed: false, churned: false),
                    Merged(5, reviewed: false, churned: false) with { SyncMerges = 0, SyncsKnown = false }
                ],
                [])
        };

        var rework = await Insights(source).GetReworkAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.True(rework.HasValue);
        Assert.Equal(3, rework.Value!.PullRequestsSynced);
        Assert.Equal(2, rework.Value.PullRequestsWithConflictedSync);
        Assert.Equal(6, rework.Value.SyncMerges);
        Assert.Equal(2, rework.Value.ConflictedSyncMerges);

        // And the churn denominator is untouched by any of it.
        Assert.Equal(1, rework.Value.PullRequestsReviewed);
    }

    /// <summary>
    /// The size buckets' edges are exact: under 10 lines is XS, under 50 S, under 200 M,
    /// under 400 L, and 400 or more XL. Each pull request lands in exactly one.
    /// </summary>
    [Theory]
    [InlineData(0, "XS")]
    [InlineData(9, "XS")]
    [InlineData(10, "S")]
    [InlineData(49, "S")]
    [InlineData(50, "M")]
    [InlineData(199, "M")]
    [InlineData(200, "L")]
    [InlineData(399, "L")]
    [InlineData(400, "XL")]
    [InlineData(5000, "XL")]
    public async Task A_sized_merge_lands_in_the_one_bucket_its_changed_lines_fall_in(int lines, string bucket)
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport([Merged(1, reviewed: true, churned: false, lines: lines)], [])
        };

        var rework = (await Insights(source).GetReworkAsync(DashboardScope.Default, TestContext.Current.CancellationToken)).Value!;

        var landed = Assert.Single(rework.BySize, size => size.PullRequestsMerged > 0);
        Assert.Equal(bucket, landed.Label);
        Assert.Equal(1, landed.PullRequestsMerged);
    }

    [Fact]
    public async Task The_five_size_buckets_come_out_in_order_with_the_empty_ones_kept()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [
                    Merged(1, reviewed: false, churned: false, lines: 3),
                    Merged(2, reviewed: false, churned: false, lines: 7),
                    Merged(3, reviewed: false, churned: false, lines: 120),
                    Merged(4, reviewed: false, churned: false, lines: 900)
                ],
                [])
        };

        var rework = (await Insights(source).GetReworkAsync(DashboardScope.Default, TestContext.Current.CancellationToken)).Value!;

        Assert.Equal(
            [("XS", 2), ("S", 0), ("M", 1), ("L", 0), ("XL", 1)],
            rework.BySize.Select(size => (size.Label, size.PullRequestsMerged)));
        Assert.Equal(
            [(0, (int?)10), (10, 50), (50, 200), (200, 400), (400, null)],
            rework.BySize.Select(size => (size.MinLines, size.MaxLinesExclusive)));
    }

    /// <summary>
    /// A bucket's came-back share has the rework rate's denominator: merged counts every
    /// sized pull request in it, but only the reviewed ones count towards the share, so a
    /// pull request that changed after merge-without-review is not churn after review.
    /// </summary>
    [Fact]
    public async Task A_buckets_share_is_of_its_reviewed_pull_requests_not_its_merged_ones()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [
                    Merged(1, reviewed: true, churned: true, lines: 80),
                    Merged(2, reviewed: true, churned: false, lines: 60),
                    Merged(3, reviewed: false, churned: false, lines: 70),
                    Merged(4, reviewed: false, churned: true, lines: 90)
                ],
                [])
        };

        var rework = (await Insights(source).GetReworkAsync(DashboardScope.Default, TestContext.Current.CancellationToken)).Value!;

        var medium = Assert.Single(rework.BySize, size => size.Label == "M");
        Assert.Equal(4, medium.PullRequestsMerged);
        Assert.Equal(2, medium.PullRequestsReviewed);
        Assert.Equal(1, medium.PullRequestsWithChurn);
        Assert.Equal(0.5m, medium.Rate);
    }

    [Fact]
    public async Task A_bucket_with_no_reviewed_pull_request_has_no_share_rather_than_a_zero_one()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [
                    Merged(1, reviewed: false, churned: false, lines: 5),
                    Merged(2, reviewed: false, churned: false, lines: 8)
                ],
                [])
        };

        var rework = (await Insights(source).GetReworkAsync(DashboardScope.Default, TestContext.Current.CancellationToken)).Value!;

        var extraSmall = Assert.Single(rework.BySize, size => size.Label == "XS");
        Assert.Equal(2, extraSmall.PullRequestsMerged);
        Assert.Null(extraSmall.Rate);
    }

    /// <summary>
    /// A merge whose size was not read is in no bucket, whatever its line count says — its
    /// zero is not a very small pull request — and the insight counts how many were left out.
    /// </summary>
    [Fact]
    public async Task A_merge_with_no_size_read_is_in_no_bucket_and_is_counted_as_left_out()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport(
                [
                    Merged(1, reviewed: true, churned: false, lines: 30),
                    Merged(2, reviewed: true, churned: true) with { ChangedLines = 30 },
                    Merged(3, reviewed: false, churned: false)
                ],
                [])
        };

        var rework = (await Insights(source).GetReworkAsync(DashboardScope.Default, TestContext.Current.CancellationToken)).Value!;

        Assert.Equal(2, rework.PullRequestsWithoutSize);
        Assert.Equal(1, rework.BySize.Sum(size => size.PullRequestsMerged));
        Assert.Equal(0, rework.BySize.Sum(size => size.PullRequestsWithChurn));
    }

    [Fact]
    public async Task The_size_buckets_are_read_from_the_focused_repository_only()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport([Merged(1, reviewed: true, churned: false, lines: 12)], [])
        };

        var rework = (await Insights(source).GetReworkAsync(
            new DashboardScope(RepositoryFocus.Of("backlog-ide")),
            TestContext.Current.CancellationToken)).Value!;

        var asked = Assert.Single(source.Requested);
        Assert.Equal("backlog-ide", asked.Alias);
        Assert.Equal(1, Assert.Single(rework.BySize, size => size.Label == "S").PullRequestsMerged);
    }

    [Fact]
    public async Task A_focus_on_a_deleted_alias_asks_no_repository_for_the_size_buckets()
    {
        var source = new StubActivitySource();

        var rework = (await Insights(source).GetReworkAsync(
            new DashboardScope(RepositoryFocus.Of("deleted-repo")),
            TestContext.Current.CancellationToken)).Value!;

        Assert.Empty(source.Requested);
        Assert.Equal(5, rework.BySize.Count);
        Assert.All(rework.BySize, size => Assert.Equal(0, size.PullRequestsMerged));
    }

    [Fact]
    public async Task A_truncated_window_keeps_its_size_buckets_and_says_they_are_a_floor()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport([Merged(1, reviewed: true, churned: true, lines: 250)], []) { Complete = false }
        };

        var rework = (await Insights(source).GetReworkAsync(DashboardScope.Default, TestContext.Current.CancellationToken)).Value!;

        Assert.False(rework.Complete);
        Assert.Equal(1, Assert.Single(rework.BySize, size => size.Label == "L").PullRequestsWithChurn);
    }

    [Fact]
    public async Task The_size_buckets_cost_no_fetch_of_their_own()
    {
        var source = new StubActivitySource
        {
            Report = new ActivityReport([Merged(1, reviewed: true, churned: false, lines: 450)], [])
        };
        var insights = Insights(source);

        _ = await insights.GetHeadlineAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        var rework = (await insights.GetReworkAsync(DashboardScope.Default, TestContext.Current.CancellationToken)).Value!;

        Assert.Equal(1, source.Calls);
        Assert.Equal(1, Assert.Single(rework.BySize, size => size.Label == "XL").PullRequestsMerged);
    }

    /// <summary>A merged pull request; <paramref name="lines"/> gives it a read size, and
    /// leaving it out leaves the size unread, as the adapter does when detail is missing.</summary>
    private static ActivityPullRequest Merged(int number, bool reviewed, bool churned, int? lines = null)
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
            ReviewTurnaround = reviewed ? TimeSpan.FromHours(6) : null,
            SyncsKnown = true,
            SizeKnown = lines is not null,
            ChangedLines = lines ?? 0
        };
    }

    private static ActivityIssue Closed(int number) =>
        new("backlog", number, Now.AddDays(-number));

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

        /// <summary>When set, answered whatever was asked, for a test about an answer the
        /// per-block default cannot describe — one with no block in it.</summary>
        public ActivityBaseline? Answer { get; init; }

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

            if (Answer is not null) return Task.FromResult(Answer);

            return Task.FromResult(new ActivityBaseline(
                [.. blocks.Select(block => new ActivityVolume(
                    block.From,
                    block.To,
                    MergedPerBlock,
                    ClosedPerBlock))],
                Complete));
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
}
