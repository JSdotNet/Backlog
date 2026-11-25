using Backlog.Infrastructure.FileSystem.Calendar;
using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The Calendar's day capacity, answered from the Roadmap's working week (local ADR 0019).
/// One worked weekday toggled off changes that date alone (§5): it answers no hours, the
/// days beside it keep the week's, and the Calendar hears of it.
/// <para>
/// Real stores over a temp directory, as <see cref="PlanningVelocityWorkingWeekTests"/>
/// uses, because the toggle is the device week's own write.
/// </para>
/// <para>
/// It pins behaviour the adapter already has, closing a coverage gap a review found; it
/// passed on its first run, which is the point of a characterization test.
/// </para>
/// </summary>
public sealed class RoadmapCalendarCapacityTests : IDisposable
{
    // A Tuesday, Wednesday and Thursday in one untouched week: Monday to Friday, nine
    // to half five.
    private static readonly DateOnly Tuesday = new(2026, 10, 13);
    private static readonly DateOnly Wednesday = new(2026, 10, 14);
    private static readonly DateOnly Thursday = new(2026, 10, 15);

    private readonly TemporaryDirectory _temp = new("roadmap-calendar-capacity-tests");

    private string _root => _temp.Combine("data");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_worked_weekday_toggled_off_answers_no_hours_and_leaves_its_neighbours_alone()
    {
        var week = new WorkingHoursSettingsStore(Path.Combine(_root, "working-hours.json"));
        // Wired as the host registers it: the settings port over the pace store, which
        // holds the device week.
        IPlanningVelocitySettings settings = new PlanningVelocitySource(new PlanningVelocitySettingsStore(
            Path.Combine(_root, "planning-velocity.json"), time: null, week));
        using var capacity = new RoadmapCalendarCapacity(settings);

        Assert.Equal(8.5m, capacity.HoursOn(Wednesday));

        var heard = 0;
        capacity.Changed += () => heard++;

        Assert.Null(settings.ToggleWorkedDay(Wednesday));

        Assert.Equal(0m, capacity.HoursOn(Wednesday));
        Assert.Equal(8.5m, capacity.HoursOn(Tuesday));
        Assert.Equal(8.5m, capacity.HoursOn(Thursday));
        Assert.Equal(1, heard);
    }

    /// <summary>
    /// A day that ends on a cent's midpoint rounds away from zero, from the duration
    /// itself. Seven hours and eighteen seconds is 7.005 hours: the double
    /// <see cref="TimeSpan.TotalHours"/> holds 7.00499999…, which a .NET 11 cast to
    /// decimal keeps, and which would round to 7.00.
    /// </summary>
    [Theory]
    [InlineData(16, 0, 18, "7.01")]
    [InlineData(16, 0, 54, "7.02")]
    [InlineData(9, 0, 18, "0.01")]
    [InlineData(9, 26, 42, "0.45")]
    public void A_day_ending_on_a_midpoint_rounds_away_from_zero(int hour, int minute, int second, string expected)
    {
        var settings = new WeekOnly(WorkingHours.Default with
        {
            Days = [.. WorkingHours.Default.Days.Select(day => day.Day == DayOfWeek.Wednesday
                ? day with { Start = new TimeOnly(9, 0), End = new TimeOnly(hour, minute, second) }
                : day)]
        });
        using var capacity = new RoadmapCalendarCapacity(settings);

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), capacity.HoursOn(Wednesday));
    }

    /// <summary>The settings port with a fixed week and nothing else.</summary>
    private sealed class WeekOnly(WorkingHours week) : IPlanningVelocitySettings
    {
        public event Action? Changed { add { } remove { } }

        public WorkingHours WorkingWeek => week;

        public decimal Manual(string? repository = null) => 7m;

        public PaceSource Source(string? repository = null) => PaceSource.Manual;

        public string? SetManual(string? typed, string? repository = null) => null;

        public string? Choose(PaceSource source, string? repository = null) => null;

        public string? SetOwn(string? typed, string? repository = null) => null;
    }
}
