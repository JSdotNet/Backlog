using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.Extensions;
using Backlog.Modules.Dashboard.UI;
using Backlog.Modules.Dashboard.UI.Parts;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The dashboard's Hours worked part (local ADR 0019, §7): the hours worked split into
/// inside and outside office hours against the hours planned, per day over four weeks and
/// per calendar week over twelve, and "unavailable" rather than a zero when the hours
/// cannot be read.
/// <para>
/// Through the module's own derivation over a scripted source, so what is asserted is
/// what a host shows: the source's days laid out by the window, and its refusals turned
/// into a reason.
/// </para>
/// </summary>
public sealed class DashboardHoursWorkedPartTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    /// <summary>Thursday 8 October 2026, noon in Amsterdam.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.FromHours(2));

    private static readonly DateOnly Monday = new(2026, 10, 5);

    private static readonly DateOnly Thursday = new(2026, 10, 8);

    private static readonly DashboardScope FourWeeks = new(Period: DashboardPeriod.FourWeeks);

    private static readonly DashboardScope TwelveWeeks = new(Period: DashboardPeriod.TwelveWeeks);

    [Fact]
    public void The_part_shows_the_split_against_the_plan_and_the_totals()
    {
        var source = new ScriptedSource
        {
            Hours =
            {
                [Monday] = (Hours(8), Hours(0.5)),
                [Thursday] = (Hours(1.5), Hours(1.5))
            }
        };
        using var context = Context(source);

        var part = context.Render<HoursWorkedPart>(parameters => parameters.Add(p => p.Scope, FourWeeks));

        Assert.Equal("Hours worked", part.Find("[data-testid='dashboard-hours-worked'] .dashboard-part__title").TextContent);

        // The totals in text, each named: no figure is carried by a colour.
        Assert.Contains("9.5h", part.Find("[data-testid='dashboard-hours-worked-inside']").TextContent, StringComparison.Ordinal);
        Assert.Contains("2.0h", part.Find("[data-testid='dashboard-hours-worked-outside']").TextContent, StringComparison.Ordinal);
        Assert.Contains("11.5h", part.Find("[data-testid='dashboard-hours-worked-actual']").TextContent, StringComparison.Ordinal);

        // 7 Sep to 8 Oct: 24 weekdays at 8.5 hours.
        Assert.Contains("204.0h", part.Find("[data-testid='dashboard-hours-worked-planned']").TextContent, StringComparison.Ordinal);

        // The chart's table is the reader's copy: inside, outside and planned per day.
        var chart = part.Find("[data-testid='dashboard-hours-worked-bars']");
        Assert.Contains("per day", chart.QuerySelector("caption")!.TextContent, StringComparison.Ordinal);

        var rows = chart.QuerySelectorAll("tbody tr");
        Assert.Equal(["Inside office hours", "Outside office hours", "Planned"], rows.Select(row => row.QuerySelector("th")!.TextContent));

        var headings = chart.QuerySelectorAll("thead th").Skip(1).Select(cell => cell.TextContent).ToList();
        Assert.Equal(32, headings.Count);
        Assert.Equal("08 Oct", headings[^1]);

        Assert.Equal("1.5h", rows[0].QuerySelectorAll("td")[^1].TextContent);
        Assert.Equal("1.5h", rows[1].QuerySelectorAll("td")[^1].TextContent);
        Assert.Equal("8.5h", rows[2].QuerySelectorAll("td")[^1].TextContent);

        // A planned mark on every column, on the one scale.
        Assert.Equal(32, chart.QuerySelectorAll(".metric-stacked-bars__target").Length);
    }

    [Fact]
    public void Twelve_weeks_reads_per_calendar_week()
    {
        var source = new ScriptedSource { Hours = { [Monday] = (Hours(8), Hours(0.5)), [Thursday] = (Hours(1.5), Hours(1.5)) } };
        using var context = Context(source);

        var part = context.Render<HoursWorkedPart>(parameters => parameters.Add(p => p.Scope, TwelveWeeks));

        var chart = part.Find("[data-testid='dashboard-hours-worked-bars']");
        Assert.Contains("per week", chart.QuerySelector("caption")!.TextContent, StringComparison.Ordinal);

        var headings = chart.QuerySelectorAll("thead th").Skip(1).Select(cell => cell.TextContent).ToList();
        Assert.Equal(13, headings.Count);
        Assert.Equal("W41", headings[^1]);

        // This week, Monday through today: four weekdays planned so far.
        var rows = chart.QuerySelectorAll("tbody tr");
        Assert.Equal("9.5h", rows[0].QuerySelectorAll("td")[^1].TextContent);
        Assert.Equal("2.0h", rows[1].QuerySelectorAll("td")[^1].TextContent);
        Assert.Equal("34.0h", rows[2].QuerySelectorAll("td")[^1].TextContent);
    }

    [Fact]
    public void A_source_that_cannot_state_the_hours_shows_unavailable_not_zero()
    {
        using var context = Context(new ScriptedSource { Unstated = true });

        var part = context.Render<HoursWorkedPart>(parameters => parameters.Add(p => p.Scope, FourWeeks));

        var frame = part.Find("[data-testid='dashboard-hours-worked']");
        Assert.Contains("unavailable", frame.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0h", frame.TextContent, StringComparison.Ordinal);
        Assert.Empty(part.FindAll("[data-testid='dashboard-hours-worked-bars']"));
    }

    [Fact]
    public void A_failed_read_shows_unavailable_with_its_reason_not_zero()
    {
        using var context = Context(new ScriptedSource { Failure = new IOException("transcript locked") });

        var part = context.Render<HoursWorkedPart>(parameters => parameters.Add(p => p.Scope, FourWeeks));

        var frame = part.Find("[data-testid='dashboard-hours-worked']");
        Assert.Contains("unavailable", frame.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("transcript locked", frame.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("0h", frame.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_part_follows_the_period_and_not_the_repository_or_the_machine()
    {
        var source = new ScriptedSource();
        using var context = Context(source);

        var part = context.Render<HoursWorkedPart>(parameters => parameters.Add(p => p.Scope, FourWeeks));

        part.Render(parameters => parameters.Add(p => p.Scope, FourWeeks with { Repositories = RepositoryFocus.Of(["backlog"]) }));
        part.Render(parameters => parameters.Add(p => p.Scope, FourWeeks with { MachineId = DashboardTestHost.MachineId }));

        Assert.Single(source.Asked);

        part.Render(parameters => parameters.Add(p => p.Scope, TwelveWeeks));

        Assert.Equal([new DateOnly(2026, 9, 7), new DateOnly(2026, 7, 13)], source.Asked.Select(asked => asked.From));
        Assert.Contains("per week", part.Find("[data-testid='dashboard-hours-worked-bars'] caption").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_note_says_what_is_counted_and_which_filters_do_not_apply()
    {
        using var context = Context(new ScriptedSource());

        var part = context.Render<HoursWorkedPart>(parameters => parameters.Add(p => p.Scope, FourWeeks));

        var note = part.Find("[data-testid='dashboard-hours-worked-note']").TextContent;
        Assert.Contains("office hours", note, StringComparison.Ordinal);
        Assert.Contains("blocked", note, StringComparison.Ordinal);
        Assert.Contains("machine filter", note, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pane_shows_the_part_in_the_sessions_section()
    {
        using var context = new BunitContext();
        _ = context.Services.AddUnavailableDashboard("backlog");

        var pane = context.Render<DashboardPane>();

        var section = pane.Find("[data-testid='dashboard-sessions-section']");
        var part = section.QuerySelector("[data-testid='dashboard-hours-worked']");
        Assert.NotNull(part);
        Assert.Contains(DashboardTestHost.UnavailableReason, part.TextContent, StringComparison.Ordinal);
    }

    private static TimeSpan Hours(double hours) => TimeSpan.FromHours(hours);

    private static BunitContext Context(IHoursWorkedSource source)
    {
        var context = new BunitContext();

        var time = new FakeTimeProvider(Now);
        time.SetLocalTimeZone(Amsterdam);

        _ = context.Services.AddUnavailableDashboard("backlog");
        context.Services.AddSingleton<TimeProvider>(time);
        context.Services.AddSingleton(source);
        context.Services.AddDashboardModule();

        return context;
    }

    /// <summary>The adapter's answer, scripted: every date in the range, the default
    /// week's planned hours, and the hours worked on the dates the test names.</summary>
    private sealed class ScriptedSource : IHoursWorkedSource
    {
        public List<(DateOnly From, DateOnly Through)> Asked { get; } = [];

        public Dictionary<DateOnly, (TimeSpan Inside, TimeSpan Outside)> Hours { get; } = [];

        public bool Unstated { get; init; }

        public Exception? Failure { get; init; }

        public Task<IReadOnlyList<HoursWorkedDay>?> ReadAsync(
            DateOnly from,
            DateOnly through,
            CancellationToken cancellationToken = default)
        {
            Asked.Add((from, through));

            if (Failure is not null) return Task.FromException<IReadOnlyList<HoursWorkedDay>?>(Failure);
            if (Unstated) return Task.FromResult<IReadOnlyList<HoursWorkedDay>?>(null);

            var days = Enumerable.Range(0, through.DayNumber - from.DayNumber + 1)
                .Select(from.AddDays)
                .Select(date =>
                {
                    var (inside, outside) = Hours.TryGetValue(date, out var worked) ? worked : default;
                    var planned = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? TimeSpan.Zero : TimeSpan.FromHours(8.5);
                    return new HoursWorkedDay(date, inside, outside, planned);
                })
                .ToList();

            return Task.FromResult<IReadOnlyList<HoursWorkedDay>?>(days);
        }
    }
}
