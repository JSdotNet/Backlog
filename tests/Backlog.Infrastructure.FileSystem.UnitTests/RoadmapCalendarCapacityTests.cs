using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Roadmap.Abstractions.Services;

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

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "roadmap-calendar-capacity-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

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
}
