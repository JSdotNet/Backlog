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

    private static BunitContext Context(int? reviewed = null)
    {
        var context = new BunitContext();
        _ = context.Services.AddUnavailableDashboard("backlog");
        context.Services.AddSingleton<IProductivityInsights>(new Productivity(reviewed));
        return context;
    }

    private sealed class Productivity(int? reviewed) : IProductivityInsights
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
            Task.FromResult(InsightResult<ReworkInsight>.Unavailable("Not used."));

        public void Invalidate(DashboardScope scope)
        {
        }
    }
}
