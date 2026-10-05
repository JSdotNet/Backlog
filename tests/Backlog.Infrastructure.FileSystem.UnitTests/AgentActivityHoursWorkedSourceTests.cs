using Backlog.Infrastructure.FileSystem.Dashboard;
using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The Dashboard's hours worked: the person's working stretches on each date, split
/// into the time inside that date's office hours and the time outside them, beside the
/// date's planned hours (local ADR 0019, §7; verification 40 and 41).
/// <para>
/// Every test runs on a named zone, for <see cref="RoadmapActualHoursTests"/>' reason:
/// a split at 04:00 or at the end of the day asserted against an unknown zone is
/// asserted against nothing.
/// </para>
/// </summary>
public sealed class AgentActivityHoursWorkedSourceTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    /// <summary>Saturday 10 October 2026, 14:00 in Amsterdam (summer time).</summary>
    private static readonly DateTimeOffset Now = At(2026, 10, 10, 14);

    private static readonly DateOnly Monday = new(2026, 10, 5);

    private static readonly DateOnly Wednesday = new(2026, 10, 7);

    private static readonly DateOnly Thursday = new(2026, 10, 8);

    private static readonly DateOnly Friday = new(2026, 10, 9);

    private static readonly DateOnly Saturday = new(2026, 10, 3);

    private static readonly DateOnly Sunday = new(2026, 10, 11);

    [Fact]
    public async Task A_stretch_past_the_end_of_the_day_splits_at_it()
    {
        // Verification 40: on a day worked from 09:00 to 17:30, 16:00 to 19:00 is an hour
        // and a half inside office hours and an hour and a half outside.
        var source = Source(Log(Session("late", turns: [On(Thursday, 16)], Run(On(Thursday, 16), On(Thursday, 19)))));

        var day = Assert.Single(await Read(source, Thursday, Thursday));

        Assert.Equal(Thursday, day.Date);
        Assert.Equal(TimeSpan.FromHours(1.5), day.Inside);
        Assert.Equal(TimeSpan.FromHours(1.5), day.Outside);
        Assert.Equal(TimeSpan.FromHours(8.5), day.Planned);
        Assert.Equal(TimeSpan.FromHours(3), day.Actual);
    }

    [Fact]
    public async Task A_stretch_before_the_start_of_the_day_splits_at_it()
    {
        var source = Source(Log(Session("early", turns: [On(Thursday, 8)], Run(On(Thursday, 8), On(Thursday, 10)))));

        var day = Assert.Single(await Read(source, Thursday, Thursday));

        Assert.Equal(TimeSpan.FromHours(1), day.Inside);
        Assert.Equal(TimeSpan.FromHours(1), day.Outside);
    }

    [Fact]
    public async Task A_blocked_date_counts_all_its_work_outside_and_plans_nothing()
    {
        // Verification 41 and the requirement's scenario: a stretch well inside the
        // weekday's stored hours, on a date the person blocked.
        var week = WorkingHours.Default.Toggled(Friday);
        var source = Source(Log(Session("leave", turns: [On(Friday, 10)], Run(On(Friday, 10), On(Friday, 12)))), week);

        var day = Assert.Single(await Read(source, Friday, Friday));

        Assert.Equal(TimeSpan.Zero, day.Inside);
        Assert.Equal(TimeSpan.FromHours(2), day.Outside);
        Assert.Equal(TimeSpan.Zero, day.Planned);
    }

    [Fact]
    public async Task An_unblocked_saturday_uses_the_stored_saturday_times()
    {
        // The pattern leaves Saturday off but keeps its hours: ten to two here. Unblocked,
        // the date counts them.
        var week = new WorkingHours
        {
            Days =
            [
                .. WorkingHours.Week
                    .Where(day => day != DayOfWeek.Saturday)
                    .Select(day => WorkingHours.Default.On(day)),
                new WorkingDay(DayOfWeek.Saturday, false, new TimeOnly(10, 0), new TimeOnly(14, 0))
            ]
        }.Toggled(Saturday);

        var source = Source(Log(Session("weekend", turns: [On(Saturday, 9)], Run(On(Saturday, 9), On(Saturday, 12)))), week);

        var day = Assert.Single(await Read(source, Saturday, Saturday));

        Assert.Equal(TimeSpan.FromHours(2), day.Inside);
        Assert.Equal(TimeSpan.FromHours(1), day.Outside);
        Assert.Equal(TimeSpan.FromHours(4), day.Planned);
    }

    [Fact]
    public async Task A_saturday_the_pattern_leaves_off_counts_all_its_work_outside()
    {
        var source = Source(Log(Session("weekend", turns: [On(Saturday, 10)], Run(On(Saturday, 10), On(Saturday, 12)))));

        var day = Assert.Single(await Read(source, Saturday, Saturday));

        Assert.Equal(TimeSpan.Zero, day.Inside);
        Assert.Equal(TimeSpan.FromHours(2), day.Outside);
        Assert.Equal(TimeSpan.Zero, day.Planned);
    }

    [Fact]
    public async Task An_evening_past_midnight_counts_outside_on_the_day_it_began()
    {
        // 23:00 Wednesday to 01:00 Thursday: two hours on Wednesday, outside.
        var source = Source(Log(Session("night", turns: [On(Wednesday, 23)], Run(On(Wednesday, 23), On(Thursday, 1)))));

        var days = await Read(source, Wednesday, Thursday);

        Assert.Equal([Wednesday, Thursday], days.Select(day => day.Date));
        Assert.Equal(TimeSpan.Zero, days[0].Inside);
        Assert.Equal(TimeSpan.FromHours(2), days[0].Outside);
        Assert.Equal(TimeSpan.Zero, days[1].Actual);
    }

    [Fact]
    public async Task A_stretch_across_four_in_the_morning_splits_onto_both_dates()
    {
        // 03:00 to 05:00 Thursday: an hour on each date, both outside.
        var source = Source(Log(Session("dawn", turns: [On(Thursday, 3)], Run(On(Thursday, 3), On(Thursday, 5)))));

        var days = await Read(source, Wednesday, Thursday);

        Assert.All(days, day =>
        {
            Assert.Equal(TimeSpan.Zero, day.Inside);
            Assert.Equal(TimeSpan.FromHours(1), day.Outside);
        });
    }

    [Fact]
    public async Task A_week_answers_every_date_and_its_days_sum_to_the_actual_hours()
    {
        var source = Source(Log(
            Session("monday", turns: [On(Monday, 9)], Run(On(Monday, 9), On(Monday, 12))),
            Session("thursday", turns: [On(Thursday, 16)], Run(On(Thursday, 16), On(Thursday, 19))),
            Session("saturday", turns: [On(new DateOnly(2026, 10, 10), 10)], Run(On(new DateOnly(2026, 10, 10), 10), On(new DateOnly(2026, 10, 10), 11)))));

        var days = await Read(source, Monday, Sunday);

        Assert.Equal(7, days.Count);
        Assert.Equal(Enumerable.Range(0, 7).Select(Monday.AddDays), days.Select(day => day.Date));
        Assert.Equal(TimeSpan.FromHours(4.5), Sum(days, day => day.Inside));
        Assert.Equal(TimeSpan.FromHours(2.5), Sum(days, day => day.Outside));
        Assert.Equal(TimeSpan.FromHours(42.5), Sum(days, day => day.Planned));
    }

    [Fact]
    public async Task Inside_and_outside_add_up_to_the_roadmaps_actual_hours()
    {
        var log = Log(
            Session("one", turns: [On(Thursday, 7)], Run(On(Thursday, 7), On(Thursday, 11))),
            Session("two", turns: [On(Thursday, 10)], Run(On(Thursday, 10), On(Thursday, 18))));

        var days = await Read(Source(log), Thursday, Thursday);
        var actual = await new RoadmapActualHours(new StubActivity(log), Clock(Now))
            .ReadAsync(Thursday, Thursday, TestContext.Current.CancellationToken);

        Assert.Equal(actual![Thursday], Assert.Single(days).Actual);
    }

    [Fact]
    public async Task A_date_not_yet_begun_counts_no_work_and_keeps_its_planned_hours()
    {
        var nextMonday = new DateOnly(2026, 10, 12);
        var source = Source(Log());

        var day = Assert.Single(await Read(source, nextMonday, nextMonday));

        Assert.Equal(TimeSpan.Zero, day.Actual);
        Assert.Equal(TimeSpan.FromHours(8.5), day.Planned);
    }

    [Fact]
    public async Task A_day_whose_end_is_not_after_its_start_has_no_office_hours()
    {
        var week = new WorkingHours
        {
            Days =
            [
                .. WorkingHours.Week
                    .Where(day => day != DayOfWeek.Thursday)
                    .Select(day => WorkingHours.Default.On(day)),
                new WorkingDay(DayOfWeek.Thursday, true, new TimeOnly(17, 0), new TimeOnly(9, 0))
            ]
        };
        var source = Source(Log(Session("odd", turns: [On(Thursday, 10)], Run(On(Thursday, 10), On(Thursday, 11)))), week);

        var day = Assert.Single(await Read(source, Thursday, Thursday));

        Assert.Equal(TimeSpan.Zero, day.Inside);
        Assert.Equal(TimeSpan.FromHours(1), day.Outside);
        Assert.Equal(TimeSpan.Zero, day.Planned);
    }

    [Fact]
    public async Task The_week_is_read_per_call_so_a_day_blocked_since_counts_at_once()
    {
        var settings = new Week(WorkingHours.Default);
        var source = new AgentActivityHoursWorkedSource(
            new StubActivity(Log(Session("any", turns: [On(Friday, 10)], Run(On(Friday, 10), On(Friday, 12))))),
            settings,
            Clock(Now));

        var before = Assert.Single((await source.ReadAsync(Friday, Friday, TestContext.Current.CancellationToken))!);
        settings.Current = WorkingHours.Default.Toggled(Friday);
        var after = Assert.Single((await source.ReadAsync(Friday, Friday, TestContext.Current.CancellationToken))!);

        Assert.Equal(TimeSpan.FromHours(2), before.Inside);
        Assert.Equal(TimeSpan.FromHours(2), after.Outside);
    }

    [Fact]
    public async Task A_host_without_session_activity_cannot_state_the_hours()
    {
        var hours = await new AgentActivityHoursWorkedSource(null, new Week(WorkingHours.Default), Clock(Now))
            .ReadAsync(Monday, Sunday, TestContext.Current.CancellationToken);

        Assert.Null(hours);
    }

    [Fact]
    public async Task With_the_sessions_area_switched_off_the_hours_are_not_read()
    {
        var activity = new StubActivity(Log(Session("any", turns: [On(Thursday, 9)], Run(On(Thursday, 9), On(Thursday, 10)))));

        var hours = await new AgentActivityHoursWorkedSource(activity, new Week(WorkingHours.Default), Clock(Now), new Features(sessions: false))
            .ReadAsync(Monday, Sunday, TestContext.Current.CancellationToken);

        Assert.Null(hours);
        Assert.Empty(activity.Asked);
    }

    [Fact]
    public async Task With_the_sessions_area_on_the_hours_are_read()
    {
        var activity = new StubActivity(Log(Session("any", turns: [On(Thursday, 9)], Run(On(Thursday, 9), On(Thursday, 10)))));

        var hours = await new AgentActivityHoursWorkedSource(activity, new Week(WorkingHours.Default), Clock(Now), new Features(sessions: true))
            .ReadAsync(Thursday, Thursday, TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromHours(1), Assert.Single(hours!).Inside);
    }

    [Fact]
    public async Task The_activity_is_read_from_a_day_before_the_first_date()
    {
        var activity = new StubActivity(Log());

        await Read(activity, Monday, Sunday);

        Assert.Equal(At(2026, 10, 4, 4), Assert.Single(activity.Asked));
    }

    [Fact]
    public async Task A_failed_read_surfaces_to_the_caller()
    {
        var activity = new StubActivity(Log()) { Failure = new IOException("transcript locked") };

        await Assert.ThrowsAsync<IOException>(() => Read(activity, Monday, Sunday));
    }

    private static TimeSpan Sum(IEnumerable<HoursWorkedDay> days, Func<HoursWorkedDay, TimeSpan> of) =>
        TimeSpan.FromTicks(days.Sum(day => of(day).Ticks));

    private static AgentActivityHoursWorkedSource Source(AgentActivityLog log, WorkingHours? week = null) =>
        new(new StubActivity(log), new Week(week ?? WorkingHours.Default), Clock(Now));

    private static Task<IReadOnlyList<HoursWorkedDay>> Read(StubActivity activity, DateOnly from, DateOnly through) =>
        Read(new AgentActivityHoursWorkedSource(activity, new Week(WorkingHours.Default), Clock(Now)), from, through);

    private static async Task<IReadOnlyList<HoursWorkedDay>> Read(
        AgentActivityHoursWorkedSource source,
        DateOnly from,
        DateOnly through)
    {
        var days = await source.ReadAsync(from, through, TestContext.Current.CancellationToken);

        Assert.NotNull(days);
        return days;
    }

    private static FakeTimeProvider Clock(DateTimeOffset now)
    {
        var time = new FakeTimeProvider(now);
        time.SetLocalTimeZone(Amsterdam);
        return time;
    }

    /// <summary>An Amsterdam wall-clock hour as the instant it names.</summary>
    private static DateTimeOffset At(int year, int month, int day, int hour)
    {
        var local = new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Amsterdam.GetUtcOffset(local));
    }

    /// <summary>An Amsterdam wall-clock time on a date as the instant it names.</summary>
    private static DateTimeOffset On(DateOnly date, int hour, int minute = 0) =>
        At(date.Year, date.Month, date.Day, hour).AddMinutes(minute);

    private static AgentActivityRun Run(DateTimeOffset from, DateTimeOffset to) => new(from, to);

    private static AgentSessionActivity Session(string id, DateTimeOffset[] turns, params AgentActivityRun[] runs) =>
        new(id, AgentSessionKind.Claude, "tower", "DEV-TOWER", runs, []) { HumanTurns = turns };

    private static AgentActivityLog Log(params AgentSessionActivity[] sessions) =>
        new(sessions, [], DateTimeOffset.MinValue, TimeSpan.FromMinutes(5));

    private sealed class StubActivity(AgentActivityLog log) : IAgentActivitySource
    {
        public List<DateTimeOffset> Asked { get; } = [];

        public Exception? Failure { get; init; }

        public Task<AgentActivityLog> GetActivityAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
        {
            Asked.Add(since);
            return Failure is null ? Task.FromResult(log) : Task.FromException<AgentActivityLog>(Failure);
        }
    }

    private sealed class Week(WorkingHours current) : IWorkingHoursSettings
    {
        public event Action? Changed { add { } remove { } }

        public WorkingHours Current { get; set; } = current;

        public string SettingsPath => "working-hours.json";

        public string? SetDay(DayOfWeek day, bool working, TimeOnly start, TimeOnly end) => null;

        public string? ResetToDefault() => null;

        public string? ToggleDate(DateOnly date) => null;

        public string? BlockDays(DateOnly from, DateOnly through) => null;

        public string? AddWorkedDay(DateOnly date) => null;

        public string? RemoveDayOverride(DateOnly date) => null;
    }

    private sealed class Features(bool sessions) : IAppFeatureSettings
    {
        public event Action? Changed { add { } remove { } }

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "features.json";

        public bool IsEnabled(string key) =>
            key == SessionFeatures.Sessions ? sessions : throw new ArgumentException("Unknown feature " + key, nameof(key));

        public string? SetEnabled(string key, bool enabled) => null;
    }
}
