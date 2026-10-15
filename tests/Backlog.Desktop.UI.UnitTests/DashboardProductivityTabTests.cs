using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI.Parts;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Productivity tab against board Productivity (S5): the throughput tiles' notes, the
/// medians drawn against the window's weeks, and why Lines changed is not in volume.
/// </summary>
public sealed class DashboardProductivityTabTests
{
    [Fact]
    public void Merged_and_closed_say_how_many_a_week()
    {
        using var context = Context();

        var part = context.Render<HeadlinePart>(parameters => parameters.Add(p => p.Scope, new DashboardScope(Period: DashboardPeriod.EightWeeks)));

        part.WaitForAssertion(() =>
        {
            Assert.Contains("18 a week", part.Find("[data-testid='dashboard-headline-pulls']").TextContent, StringComparison.Ordinal);
            Assert.Contains("11.5 a week", part.Find("[data-testid='dashboard-headline-issues']").TextContent, StringComparison.Ordinal);
        });
    }

    /// <summary>A median with no reading in W1 and W3 keeps its two points over W2 and
    /// W4, a third of the way along and at the end, rather than closing up.</summary>
    [Fact]
    public void A_median_with_gaps_is_drawn_across_the_windows_weeks()
    {
        using var context = Context();

        var part = context.Render<HeadlinePart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        part.WaitForAssertion(() => Assert.Equal(
            "33.33,15 100,0",
            part.Find("[data-testid='dashboard-headline-turnaround-trend'] polyline").GetAttribute("points")));
    }

    /// <summary>"Came back after review" with no reviewed merge reads "—", as the
    /// Overview's throughput does - verify read "0%" here beside a First review of "—".</summary>
    [Fact]
    public void Came_back_after_review_is_a_dash_when_nothing_was_reviewed()
    {
        using var context = Context(reviewed: 0);

        var part = context.Render<HeadlinePart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        part.WaitForAssertion(() =>
        {
            var tile = part.Find("[data-testid='dashboard-headline-rework']").TextContent;
            Assert.Contains("—", tile, StringComparison.Ordinal);
            Assert.DoesNotContain("0%", tile, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_score_says_why_lines_changed_is_not_part_of_volume()
    {
        using var context = Context();

        var part = context.Render<ScorePart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        Assert.Contains("Lines changed is not part of volume", part.Markup, StringComparison.Ordinal);
    }

    /// <summary>Size and rework draws merged per bucket as columns, XS to XL, and
    /// each bucket's came-back share as a tile.</summary>
    [Fact]
    public void Size_and_rework_charts_merged_by_size_and_tiles_each_buckets_share()
    {
        using var context = Context(rework: InsightResult<ReworkInsight>.Ready(Sized()));

        var part = context.Render<SizeReworkPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        part.WaitForAssertion(() =>
        {
            var rows = part.FindAll("[data-testid='dashboard-size-bars'] tbody tr")
                .Select(row => (row.Children[0].TextContent.Trim(), row.Children[1].TextContent.Trim()))
                .ToList();
            Assert.Equal([("XS", "8"), ("S", "12"), ("M", "0"), ("L", "4"), ("XL", "3")], rows);

            var small = Squashed(part.Find("[data-testid='dashboard-size-s']").TextContent);
            Assert.Contains("S · 10–49 lines", small, StringComparison.Ordinal);
            Assert.Contains("25%", small, StringComparison.Ordinal);

            // The share stands alone on its line and the count goes under it: side by
            // side in a narrow tile the share broke mid-number ("33.3" / "%").
            Assert.Equal("25%", Squashed(part.Find("[data-testid='dashboard-size-s'] .metric-tile__value").TextContent));
            Assert.Equal("of 8 reviewed", Squashed(part.Find("[data-testid='dashboard-size-s'] .metric-tile__footnote").TextContent));

            Assert.Contains("under 10 lines", part.Find("[data-testid='dashboard-size-xs']").TextContent, StringComparison.Ordinal);
            Assert.Contains("400+ lines", part.Find("[data-testid='dashboard-size-xl']").TextContent, StringComparison.Ordinal);
        });
    }

    /// <summary>A bucket whose pull requests nobody reviewed has no share: "—", as the
    /// headline's "Came back after review" tile reads, never "0%".</summary>
    [Fact]
    public void A_size_bucket_nobody_reviewed_shows_a_dash_rather_than_zero()
    {
        using var context = Context(rework: InsightResult<ReworkInsight>.Ready(Sized()));

        var part = context.Render<SizeReworkPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        part.WaitForAssertion(() =>
        {
            var large = part.Find("[data-testid='dashboard-size-l']").TextContent;
            Assert.Contains("—", large, StringComparison.Ordinal);
            Assert.Equal("—", Squashed(part.Find("[data-testid='dashboard-size-l'] .metric-tile__value").TextContent));
            Assert.Equal("none reviewed", Squashed(part.Find("[data-testid='dashboard-size-l'] .metric-tile__footnote").TextContent));
            Assert.DoesNotContain("0%", large, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Size_and_rework_says_how_many_merges_had_no_size_read()
    {
        using var context = Context(rework: InsightResult<ReworkInsight>.Ready(Sized() with { PullRequestsWithoutSize = 3 }));

        var part = context.Render<SizeReworkPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        part.WaitForAssertion(() => Assert.Contains(
            "3 merged pull requests have no size read",
            part.Find("[data-testid='dashboard-size-unknown']").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Size_and_rework_says_nothing_about_unsized_merges_when_there_are_none()
    {
        using var context = Context(rework: InsightResult<ReworkInsight>.Ready(Sized()));

        var part = context.Render<SizeReworkPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        part.WaitForAssertion(() => Assert.NotEmpty(part.FindAll("[data-testid='dashboard-size-bars']")));
        Assert.Empty(part.FindAll("[data-testid='dashboard-size-unknown']"));
    }

    [Fact]
    public void A_truncated_window_makes_every_size_figure_a_floor()
    {
        using var context = Context(rework: InsightResult<ReworkInsight>.Ready(Sized() with { Complete = false }));

        var part = context.Render<SizeReworkPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        part.WaitForAssertion(() => Assert.Contains(
            "Some of this window could not be read, so every figure here is a floor.",
            part.Markup,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Size_and_rework_shows_the_sources_reason_when_it_cannot_answer()
    {
        using var context = Context(rework: InsightResult<ReworkInsight>.Unavailable("GitHub is not signed in."));

        var part = context.Render<SizeReworkPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        part.WaitForAssertion(() => Assert.Contains(
            "GitHub is not signed in.",
            part.Find("[data-testid='dashboard-size-status']").TextContent,
            StringComparison.Ordinal));
        Assert.Empty(part.FindAll("[data-testid='dashboard-size-bars']"));
    }

    [Fact]
    public void Size_and_rework_is_empty_when_no_merge_had_its_size_read()
    {
        var unsized = new ReworkInsight(0, 0, 0, 0, 0, true, [])
        {
            BySize =
            [
                new ReworkSize("XS", 0, 10, 0, 0, 0),
                new ReworkSize("S", 10, 50, 0, 0, 0),
                new ReworkSize("M", 50, 200, 0, 0, 0),
                new ReworkSize("L", 200, 400, 0, 0, 0),
                new ReworkSize("XL", 400, null, 0, 0, 0)
            ],
            PullRequestsWithoutSize = 4
        };
        using var context = Context(rework: InsightResult<ReworkInsight>.Ready(unsized));

        var part = context.Render<SizeReworkPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        part.WaitForAssertion(() => Assert.Contains(
            "had its size read",
            part.Find("[data-testid='dashboard-size-status']").TextContent,
            StringComparison.Ordinal));
        Assert.Empty(part.FindAll("[data-testid='dashboard-size-bars']"));
    }

    /// <summary>A window that merged nothing is not a size read that failed: the empty
    /// line says nothing merged rather than sending the reader looking for a fault.</summary>
    [Fact]
    public void Size_and_rework_says_nothing_merged_when_the_window_merged_nothing()
    {
        var nothingMerged = new ReworkInsight(0, 0, 0, 0, 0, true, [])
        {
            BySize =
            [
                new ReworkSize("XS", 0, 10, 0, 0, 0),
                new ReworkSize("S", 10, 50, 0, 0, 0),
                new ReworkSize("M", 50, 200, 0, 0, 0),
                new ReworkSize("L", 200, 400, 0, 0, 0),
                new ReworkSize("XL", 400, null, 0, 0, 0)
            ]
        };
        using var context = Context(rework: InsightResult<ReworkInsight>.Ready(nothingMerged));

        var part = context.Render<SizeReworkPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        part.WaitForAssertion(() =>
        {
            var status = part.Find("[data-testid='dashboard-size-status']").TextContent;
            Assert.Contains("No pull request merged in this window", status, StringComparison.Ordinal);
            Assert.DoesNotContain("size read", status, StringComparison.Ordinal);
        });
    }

    /// <summary>Five buckets with every shape a tile has to say: an empty bucket, one
    /// whose pull requests nobody reviewed, and a share of a quarter.</summary>
    private static ReworkInsight Sized() =>
        new(6, 22, 12, 2, 9, true, [])
        {
            BySize =
            [
                new ReworkSize("XS", 0, 10, 8, 6, 0),
                new ReworkSize("S", 10, 50, 12, 8, 2),
                new ReworkSize("M", 50, 200, 0, 0, 0),
                new ReworkSize("L", 200, 400, 4, 0, 0),
                new ReworkSize("XL", 400, null, 3, 2, 1)
            ]
        };

    private static string Squashed(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static BunitContext Context(int? reviewed = null, InsightResult<ReworkInsight>? rework = null)
    {
        var context = new BunitContext();
        _ = context.Services.AddUnavailableDashboard("backlog");
        context.Services.AddSingleton<IProductivityInsights>(new Productivity(reviewed, rework));
        return context;
    }

    private sealed class Productivity(int? reviewed, InsightResult<ReworkInsight>? rework) : IProductivityInsights
    {
        public Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityHeadline>.Ready(new ProductivityHeadline(
                144, 92, 0.17m, TimeSpan.FromHours(3),
                [new("W1", 30m), new("W2", 40m), new("W3", 34m), new("W4", 40m)],
                [new("W1", 20m), new("W2", 30m), new("W3", 22m), new("W4", 20m)],
                [])
            {
                ReviewTurnaroundPerWeek = [new("W2", 2m), new("W4", 4m)],
                CommitsPerPullRequestPerWeek = [new("W2", 3m), new("W4", 5m)],
                PullRequestsReviewed = reviewed
            } with { ReworkRate = reviewed == 0 ? 0m : 0.17m }));

        public Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityScoreInsight>.Unavailable("Not used."));

        public Task<InsightResult<ReworkInsight>> GetReworkAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(rework ?? InsightResult<ReworkInsight>.Unavailable("Not used."));

        public void Invalidate(DashboardScope scope)
        {
        }
    }
}
