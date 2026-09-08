using Backlog.Infrastructure.FileSystem;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The reader's working week on disk: what it says before anybody has said
/// anything, what survives a restart, and what it does with a file somebody has
/// edited by hand.
/// <para>
/// Real temp directories rather than a filesystem abstraction, the way
/// <see cref="PullRequestDetailCacheTests"/> does it and for the same reason —
/// what is under test <em>is</em> the file, down to the format the times are
/// written in, and a fake would be asserting the fake.
/// </para>
/// </summary>
public class WorkingHoursSettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "working-hours-settings-store-tests-" + Guid.NewGuid().ToString("N"));

    public WorkingHoursSettingsStoreTests() => Directory.CreateDirectory(_root);

    private string SettingsFile => Path.Combine(_root, "working-hours.json");

    private WorkingHoursSettingsStore Store() => new(SettingsFile);

    [Fact]
    public void An_untouched_week_is_Monday_to_Friday_nine_to_half_five()
    {
        var store = Store();

        foreach (var day in new[]
        {
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday
        })
        {
            var working = store.Current.On(day);

            Assert.True(working.Working);
            Assert.Equal(new TimeOnly(9, 0), working.Start);
            Assert.Equal(new TimeOnly(17, 30), working.End);
        }
    }

    [Fact]
    public void An_untouched_weekend_is_not_worked()
    {
        var store = Store();

        Assert.False(store.Current.On(DayOfWeek.Saturday).Working);
        Assert.False(store.Current.On(DayOfWeek.Sunday).Working);
    }

    [Fact]
    public void A_day_that_was_changed_still_says_so_after_a_restart()
    {
        Assert.Null(Store().SetDay(DayOfWeek.Friday, working: true, new TimeOnly(7, 0), new TimeOnly(13, 0)));

        var reopened = Store().Current.On(DayOfWeek.Friday);

        Assert.True(reopened.Working);
        Assert.Equal(new TimeOnly(7, 0), reopened.Start);
        Assert.Equal(new TimeOnly(13, 0), reopened.End);
    }

    /// <summary>The days nobody touched are still the default ones. A week is
    /// seven independent choices, so saving one of them is not a claim about the
    /// other six.</summary>
    [Fact]
    public void Changing_one_day_leaves_the_rest_alone()
    {
        var store = Store();

        _ = store.SetDay(DayOfWeek.Friday, working: true, new TimeOnly(7, 0), new TimeOnly(13, 0));

        Assert.Equal(new TimeOnly(9, 0), Store().Current.On(DayOfWeek.Monday).Start);
        Assert.False(Store().Current.On(DayOfWeek.Sunday).Working);
    }

    [Fact]
    public void A_day_that_ends_before_it_starts_is_refused_and_nothing_is_stored()
    {
        var store = Store();

        var refusal = store.SetDay(DayOfWeek.Monday, working: true, new TimeOnly(17, 0), new TimeOnly(9, 0));

        Assert.NotNull(refusal);
        Assert.Equal(new TimeOnly(9, 0), store.Current.On(DayOfWeek.Monday).Start);
        Assert.Equal(new TimeOnly(17, 30), store.Current.On(DayOfWeek.Monday).End);
        Assert.False(File.Exists(SettingsFile));
    }

    /// <summary>A day of no length is the same refusal as a day of negative
    /// length: <see cref="WorkingHours.Covers"/> marks nothing for either, and a
    /// row that silently marked nothing would be a setting that did not work.</summary>
    [Fact]
    public void A_day_that_ends_when_it_starts_is_refused_too()
    {
        var store = Store();

        Assert.NotNull(store.SetDay(DayOfWeek.Monday, working: true, new TimeOnly(9, 0), new TimeOnly(9, 0)));
    }

    [Fact]
    public void The_refusal_says_which_hours_it_will_not_take()
    {
        var refusal = Store().SetDay(DayOfWeek.Monday, working: true, new TimeOnly(17, 0), new TimeOnly(9, 0));

        Assert.NotNull(refusal);
        Assert.Contains("17:00", refusal);
        Assert.Contains("09:00", refusal);
    }

    [Fact]
    public void A_file_nobody_can_read_falls_back_to_the_default_week()
    {
        File.WriteAllText(SettingsFile, "{ this is not json");

        var store = Store();

        Assert.True(store.Current.On(DayOfWeek.Monday).Working);
        Assert.Equal(new TimeOnly(9, 0), store.Current.On(DayOfWeek.Monday).Start);
    }

    /// <summary>A missing day is filled from the default rather than read as a
    /// day off. Nothing was said about it, and saying "not a working day" on the
    /// reader's behalf is the assumption this setting exists to avoid.</summary>
    [Fact]
    public void A_day_the_file_does_not_mention_comes_from_the_default()
    {
        File.WriteAllText(
            SettingsFile,
            """{ "days": [ { "day": "Monday", "working": true, "start": "07:00", "end": "12:00" } ] }""");

        var store = Store();

        Assert.Equal(new TimeOnly(7, 0), store.Current.On(DayOfWeek.Monday).Start);
        Assert.True(store.Current.On(DayOfWeek.Wednesday).Working);
        Assert.Equal(new TimeOnly(17, 30), store.Current.On(DayOfWeek.Wednesday).End);
        Assert.False(store.Current.On(DayOfWeek.Saturday).Working);
    }

    [Fact]
    public void A_hand_edited_file_is_read_back_as_the_whole_week_in_reading_order()
    {
        File.WriteAllText(
            SettingsFile,
            """{ "days": [ { "day": "Sunday", "working": true, "start": "10:00", "end": "12:00" } ] }""");

        var store = Store();

        Assert.Equal(WorkingHours.Week, store.Current.Days.Select(day => day.Day).ToArray());
    }

    [Fact]
    public void A_day_turned_off_keeps_its_hours_for_when_it_comes_back()
    {
        var store = Store();

        _ = store.SetDay(DayOfWeek.Saturday, working: true, new TimeOnly(10, 0), new TimeOnly(14, 0));
        _ = store.SetDay(DayOfWeek.Saturday, working: false, new TimeOnly(10, 0), new TimeOnly(14, 0));

        var off = Store().Current.On(DayOfWeek.Saturday);

        Assert.False(off.Working);
        Assert.Equal(new TimeOnly(10, 0), off.Start);
        Assert.Equal(new TimeOnly(14, 0), off.End);
    }

    [Fact]
    public void Saving_tells_whoever_is_showing_the_week()
    {
        var store = Store();
        var announced = 0;
        store.Changed += () => announced++;

        _ = store.SetDay(DayOfWeek.Monday, working: true, new TimeOnly(8, 0), new TimeOnly(16, 0));

        Assert.Equal(1, announced);
    }

    /// <summary>Nothing changed is nothing to announce. A screen redrawing on a
    /// value it already had is work nobody asked for.</summary>
    [Fact]
    public void Storing_the_hours_a_day_already_has_announces_nothing()
    {
        var store = Store();
        var announced = 0;
        store.Changed += () => announced++;

        Assert.Null(store.SetDay(DayOfWeek.Monday, working: true, new TimeOnly(9, 0), new TimeOnly(17, 30)));
        Assert.Equal(0, announced);
    }

    [Fact]
    public void Going_back_to_the_default_puts_the_whole_week_back()
    {
        var store = Store();
        _ = store.SetDay(DayOfWeek.Monday, working: false, new TimeOnly(9, 0), new TimeOnly(17, 30));
        _ = store.SetDay(DayOfWeek.Saturday, working: true, new TimeOnly(10, 0), new TimeOnly(14, 0));

        Assert.Null(store.ResetToDefault());

        var reopened = Store().Current;

        Assert.True(reopened.On(DayOfWeek.Monday).Working);
        Assert.False(reopened.On(DayOfWeek.Saturday).Working);
        Assert.Equal(new TimeOnly(9, 0), reopened.On(DayOfWeek.Saturday).Start);
    }

    /// <summary>The file is meant to be read and hand-edited, and
    /// <c>HH:mm</c> is what a person writes - not the round-trip form, which
    /// would carry a precision nobody chose.</summary>
    [Fact]
    public void Times_are_written_as_hours_and_minutes()
    {
        _ = Store().SetDay(DayOfWeek.Monday, working: true, new TimeOnly(8, 5), new TimeOnly(16, 45));

        var written = File.ReadAllText(SettingsFile);

        Assert.Contains("\"08:05\"", written);
        Assert.Contains("\"16:45\"", written);
        Assert.DoesNotContain("08:05:00", written);
    }

    [Fact]
    public void What_was_written_comes_back_as_the_same_times()
    {
        _ = Store().SetDay(DayOfWeek.Thursday, working: true, new TimeOnly(8, 5), new TimeOnly(16, 45));

        var reopened = Store().Current.On(DayOfWeek.Thursday);

        Assert.Equal(new TimeOnly(8, 5), reopened.Start);
        Assert.Equal(new TimeOnly(16, 45), reopened.End);
    }

    /// <summary>An inverted range is left exactly as the hand-edit wrote it.
    /// The setter refuses one, so it can only arrive that way, and
    /// <see cref="WorkingHours.Covers"/> already says what it means: nothing is
    /// marked. Repairing it would invent an end nobody chose.</summary>
    [Fact]
    public void A_hand_edited_backwards_day_is_left_alone_and_marks_nothing()
    {
        File.WriteAllText(
            SettingsFile,
            """{ "days": [ { "day": "Monday", "working": true, "start": "17:00", "end": "09:00" } ] }""");

        var store = Store();

        Assert.Equal(new TimeOnly(17, 0), store.Current.On(DayOfWeek.Monday).Start);
        Assert.Equal(new TimeOnly(9, 0), store.Current.On(DayOfWeek.Monday).End);
        Assert.False(store.Current.Covers(DayOfWeek.Monday, 12));
    }

    [Fact]
    public void The_store_says_where_the_week_is_kept()
    {
        Assert.Equal(SettingsFile, Store().SettingsPath);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
