using System.Globalization;
using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI.Parts;
using Backlog.SharedKernel;
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

    // --- Which usage reset cuts the weeks ------------------------------------------

    /// <summary>With no reset configured there is nothing to choose between, and the
    /// sentence says what stands in, as it always has.</summary>
    [Fact]
    public void With_no_reset_configured_there_is_no_reset_picker()
    {
        var part = Render(out var context);
        using var _ = context;

        Assert.Empty(part.FindAll("[data-testid='dashboard-sessions-reset']"));
        Assert.Contains("Calendar weeks from Monday", Squashed(part.Find("[data-testid='dashboard-sessions-weekly-bars']").TextContent), StringComparison.Ordinal);
    }

    /// <summary>One reset is not a choice, so there is no picker - but the sentences
    /// name it, so a reader knows whose allowance the weeks are.</summary>
    [Fact]
    public void With_one_reset_there_is_no_picker_and_the_sentences_name_it()
    {
        var resets = new ListedResets(Work);
        var part = RenderOn(resets, out var context, out var insights);
        using var _ = context;

        Assert.Empty(part.FindAll("[data-testid='dashboard-sessions-reset']"));
        Assert.Contains(
            "Weeks run from Monday 14:00 to the next, Work's usage reset",
            Squashed(part.Find("[data-testid='dashboard-sessions-weekly-bars']").TextContent),
            StringComparison.Ordinal);
        Assert.Contains("the columns run from Work's reset (Monday 14:00)", Squashed(part.Markup), StringComparison.Ordinal);
        Assert.Equal(1, insights.Calls);
    }

    /// <summary>Two resets are a choice: the picker lists them by name, first among the
    /// chart's controls, and picking one re-cuts the weeks on it without asking the
    /// sources to read again.</summary>
    [Fact]
    public void With_two_resets_the_picker_lists_them_and_picking_one_re_cuts_the_weeks()
    {
        var resets = new ListedResets(Work, Personal);
        var part = RenderOn(resets, out var context, out var insights);
        using var _ = context;

        var picker = part.Find("[data-testid='dashboard-sessions-reset']");
        Assert.Contains("badge--area", picker.ClassList);
        Assert.Equal(["Work", "Personal"], picker.QuerySelectorAll("option").Select(option => option.TextContent.Trim()));
        Assert.Equal("dashboard-sessions-reset", part.Find(".dashboard-sessions__controls [data-testid]").GetAttribute("data-testid"));

        part.Find("[data-testid='dashboard-sessions-reset'] select").Change("personal");

        part.WaitForAssertion(() => Assert.Contains(
            "Weeks run from Thursday 12:00 to the next, Personal's usage reset",
            Squashed(part.Find("[data-testid='dashboard-sessions-weekly-bars']").TextContent),
            StringComparison.Ordinal));

        Assert.Equal("personal", resets.Selected?.Id);
        Assert.Equal(0, insights.Invalidations);
        Assert.Equal(2, insights.Calls);
    }

    /// <summary>
    /// The Overview's agent-time tile draws the usage-week series too, and its tab stays
    /// mounted behind the Sessions tab: a pick there re-cuts the tile as well, from the
    /// same reading, so the two never show weeks cut on different resets.
    /// </summary>
    [Fact]
    public void Picking_a_reset_re_cuts_the_overview_agent_time_tile_too()
    {
        var resets = new ListedResets(Work, Personal);
        var part = RenderOn(resets, out var context, out var insights);
        using var _ = context;
        var tile = context.Render<OverviewAgentTimeTile>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        var before = tile.Find(".dashboard-overview-tile__value").TextContent.Trim();

        part.Find("[data-testid='dashboard-sessions-reset'] select").Change("personal");

        tile.WaitForAssertion(() =>
            Assert.StartsWith("12h", tile.Find(".dashboard-overview-tile__value").TextContent.Trim(), StringComparison.Ordinal));
        Assert.DoesNotContain("12h", before, StringComparison.Ordinal);

        // Two parts, each loaded once and re-cut once - and nothing read again.
        Assert.Equal(4, insights.Calls);
        Assert.Equal(0, insights.Invalidations);
    }

    /// <summary>A pick the store refuses - the reset went in the meantime - says so on
    /// the part, and the part reloads to show the list as it now is.</summary>
    [Fact]
    public void A_refused_pick_says_why_and_reloads()
    {
        var resets = new ListedResets(Work, Personal) { Refusal = "That reset is no longer there." };
        var part = RenderOn(resets, out var context, out var insights);
        using var _ = context;

        part.Find("[data-testid='dashboard-sessions-reset'] select").Change("personal");

        part.WaitForAssertion(() => Assert.Equal(
            "That reset is no longer there.",
            part.Find("[data-testid='dashboard-sessions-reset-status']").TextContent.Trim()));
        Assert.Equal("work", resets.Selected?.Id);
        Assert.Equal(2, insights.Calls);
    }

    private static readonly NamedUsageReset Work = new("work", "Work", new UsageWeekReset(DayOfWeek.Monday, new TimeOnly(14, 0)));

    private static readonly NamedUsageReset Personal = new("personal", "Personal", new UsageWeekReset(DayOfWeek.Thursday, new TimeOnly(12, 0)));

    private static string Squashed(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static IRenderedComponent<SessionsPart> RenderOn(ListedResets resets, out BunitContext context, out ResetCutSessions insights)
    {
        context = new BunitContext();
        _ = context.Services.AddUnavailableDashboard("backlog");
        insights = new ResetCutSessions(Insight(), resets);
        context.Services.AddSingleton<IUsageResetSettings>(resets);
        context.Services.AddSingleton<ISessionInsights>(insights);

        return context.Render<SessionsPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));
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

    /// <summary>Resets held in memory, and a choice between them that the picker moves.</summary>
    private sealed class ListedResets(params NamedUsageReset[] resets) : IUsageResetSettings
    {
        private string? _selected;

        public event Action? Changed;

        public IReadOnlyList<NamedUsageReset> Resets => resets;

        public NamedUsageReset? Selected => resets.FirstOrDefault(reset => reset.Id == _selected) ?? resets.FirstOrDefault();

        public string SettingsPath => "usage-reset.json";

        public (string? Id, string? Error) Add(string name, DayOfWeek day, TimeOnly time) => (null, null);

        public string? Update(string id, string name, DayOfWeek day, TimeOnly time) => null;

        public string? Remove(string id) => null;

        /// <summary>When set, Select refuses with this message and changes nothing.</summary>
        public string? Refusal { get; init; }

        public string? Select(string id)
        {
            if (Refusal is not null) return Refusal;

            _selected = id;
            Changed?.Invoke();

            return null;
        }
    }

    /// <summary>The insight's half of the cut: the week it reports is the chosen reset's,
    /// derived on every call the way the real insight derives it, with the calls and the
    /// invalidations counted so a re-read would show.</summary>
    private sealed class ResetCutSessions(AssistantSessionsInsight insight, IUsageResetSettings resets) : ISessionInsights
    {
        public int Calls { get; private set; }

        public int Invalidations { get; private set; }

        public Task<InsightResult<AssistantSessionsInsight>> GetSessionsAsync(DashboardScope scope, CancellationToken cancellationToken = default)
        {
            Calls++;

            var week = resets.Selected is { } selected
                ? new UsageWeekInfo(WeekSource.Configured, $"{selected.Reset.Day} {selected.Reset.Time.ToString("HH:mm", CultureInfo.InvariantCulture)}", selected.Name)
                : new UsageWeekInfo(WeekSource.Calendar, null);

            // A figure that differs by reset, so a part that re-cut shows it: the
            // Overview tile's headline stands in for its usage-week sparkline, which
            // draws no text.
            var active = resets.Selected?.Id == "personal" ? TimeSpan.FromHours(12) : insight.ActiveTime;

            return Task.FromResult(InsightResult<AssistantSessionsInsight>.Ready(insight with { Week = week, ActiveTime = active }));
        }

        public void Invalidate() => Invalidations++;
    }

    private sealed class Sessions(AssistantSessionsInsight insight) : ISessionInsights
    {
        public Task<InsightResult<AssistantSessionsInsight>> GetSessionsAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<AssistantSessionsInsight>.Ready(insight));

        public void Invalidate()
        {
        }
    }
}
