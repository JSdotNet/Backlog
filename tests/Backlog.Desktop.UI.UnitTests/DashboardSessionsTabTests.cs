using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI.Parts;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Sessions tab against board Sessions (S7; AC20–AC22; decisions Q1 (b), Q2 and Q4):
/// agent-active hours By model first, how a session's hours were given to a model, the
/// grid from 06 to 23 titled with its week, longest sessions to the minute, "All N
/// sessions", and the limit hits that are left out.
/// </summary>
public sealed class DashboardSessionsTabTests
{
    private static readonly DateOnly Monday12October = new(2026, 10, 12);

    [Fact]
    public void The_chart_opens_on_agent_active_hours_cut_by_model()
    {
        var part = Render(out var context);
        using var _ = context;

        var cut = part.Find("[data-testid='dashboard-sessions-cut']");
        Assert.Equal("radiogroup", cut.GetAttribute("role"));
        Assert.Equal(
            ["By model", "By repository", "All"],
            cut.QuerySelectorAll("[role='radio']").Select(radio => radio.TextContent.Trim()));
        Assert.Equal("true", part.Find("[data-testid='dashboard-sessions-cut-model']").GetAttribute("aria-checked"));

        var chart = part.Find("[data-testid='dashboard-sessions-weekly-bars']");
        Assert.Contains("Agent-active hours per week, by model", chart.TextContent, StringComparison.Ordinal);
        Assert.Contains("opus", chart.TextContent, StringComparison.Ordinal);
        Assert.Contains(AssistantSessionsInsight.ModelUnrecordedName, chart.TextContent, StringComparison.Ordinal);
    }

    /// <summary>Decision Q2: the caption says each session's hours went to its main
    /// model, because the records split tokens by model and never time.</summary>
    [Fact]
    public void The_by_model_caption_says_how_hours_were_given_to_a_model()
    {
        var part = Render(out var context);
        using var _ = context;

        Assert.Contains(
            "Each session's agent-active hours go to the model that wrote most of its output",
            part.Find("[data-testid='dashboard-sessions-weekly-bars']").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_grid_runs_from_six_to_midnight_titled_with_its_week_and_an_office_hours_key()
    {
        var part = Render(out var context);
        using var _ = context;

        var grid = part.Find("[data-testid='dashboard-sessions-hours']");
        var hours = grid.QuerySelectorAll(".metric-heatmap__bucket").Select(bucket => bucket.TextContent.Trim()).ToList();

        Assert.Equal("06", hours[0]);
        Assert.Equal("23", hours[^1]);
        Assert.Equal(18, hours.Count);
        Assert.Contains("When the agent worked · week 42", grid.TextContent, StringComparison.Ordinal);
        Assert.Contains("Office hours", grid.TextContent, StringComparison.Ordinal);
    }

    /// <summary>A week cut at midnight east of UTC starts the evening before in UTC; the
    /// title names the week on the clock it was cut on (review, Batch C).</summary>
    [Fact]
    public void The_grid_title_names_the_week_on_the_clock_it_was_cut_on()
    {
        using var context = new BunitContext();
        _ = context.Services.AddUnavailableDashboard("backlog");
        context.Services.AddSingleton<ISessionInsights>(new Sessions(Insight() with { Grids = [Grid(TimeSpan.FromHours(2))] }));

        var part = context.Render<SessionsPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        Assert.Contains("When the agent worked · week 42", part.Find("[data-testid='dashboard-sessions-hours']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_longest_sessions_read_to_the_minute_and_link_to_all_of_them()
    {
        var part = Render(out var context, opened: () => { });
        using var _ = context;

        var first = part.Find("[data-testid='dashboard-sessions-longest'] .metric-ranking__value");
        Assert.Equal("2 h 40 m", first.TextContent.Trim());
        Assert.Equal("All 64 sessions →", part.Find("[data-testid='dashboard-sessions-all']").TextContent.Trim());
    }

    /// <summary>Decision Q1 (b): refusals an older Claude Code wrote with no reset time
    /// are left out of the count, and the tile says so.</summary>
    [Fact]
    public void The_limit_hits_tile_says_unstamped_refusals_are_not_counted()
    {
        var part = Render(out var context);
        using var _ = context;

        Assert.Contains(
            "Refusals an older Claude Code wrote without a reset time are not counted",
            part.Find("[data-testid='dashboard-sessions-limit-hits']").TextContent,
            StringComparison.Ordinal);
    }

    private static IRenderedComponent<SessionsPart> Render(out BunitContext context, Action? opened = null)
    {
        context = new BunitContext();
        _ = context.Services.AddUnavailableDashboard("backlog");
        context.Services.AddSingleton<ISessionInsights>(new Sessions(Insight()));

        return opened is null
            ? context.Render<SessionsPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default))
            : context.Render<SessionsPart>(parameters => parameters
                .Add(p => p.Scope, DashboardScope.Default)
                .Add(p => p.OnOpenSessions, opened));
    }

    private static AssistantSessionsInsight Insight() =>
        new(64, TimeSpan.FromHours(38), new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero), 0, false, [], [])
        {
            SessionsPerWeek = [new InsightPoint("W41", 30m), new InsightPoint("W42", 34m)],
            ActiveTimePerWeek = [new InsightPoint("W41", 20m), new InsightPoint("W42", 18m)],
            ActiveTimeByModel =
            [
                new WeeklyBand("opus", null, [new InsightPoint("W41", 15m), new InsightPoint("W42", 14m)], 29m),
                new WeeklyBand(AssistantSessionsInsight.ModelUnrecordedName, null, [new InsightPoint("W41", 5m), new InsightPoint("W42", 4m)], 9m)
            ],
            Grids = [Grid()],
            LongestSessions =
            [
                new LongestSession("a", "Token store", "backlog", "opus", 31, new TimeSpan(2, 40, 10), DateTimeOffset.UnixEpoch)
            ],
            LimitHits = new LimitHitCounts(2, 0),
            Week = new UsageWeekInfo(WeekSource.Calendar, null),
            IdleAfter = TimeSpan.FromMinutes(5)
        };

    private static WeekGrid Grid(TimeSpan? offset = null) =>
        new(
            "2026-W42",
            "W42",
            new DateTimeOffset(Monday12October.ToDateTime(TimeOnly.MinValue), offset ?? TimeSpan.Zero),
            [
                .. Enumerable.Range(0, 7).SelectMany(day => Enumerable.Range(0, 24).Select(hour => new ActivityHour(
                    Monday12October.AddDays(day),
                    hour,
                    hour == 10 ? 2 : 0,
                    hour == 10 ? TimeSpan.FromMinutes(50) : TimeSpan.Zero,
                    TimeSpan.Zero,
                    hour == 10 ? 2 : 0,
                    day < 5 && hour is >= 9 and < 17)))
            ],
            [],
            []);

    private sealed class Sessions(AssistantSessionsInsight insight) : ISessionInsights
    {
        public Task<InsightResult<AssistantSessionsInsight>> GetSessionsAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<AssistantSessionsInsight>.Ready(insight));

        public void Invalidate()
        {
        }
    }
}
