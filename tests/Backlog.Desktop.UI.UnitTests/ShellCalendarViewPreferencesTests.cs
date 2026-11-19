using System.Text.Json;

using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Calendar.UI;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Calendar's own view choices (local ADR 0055 §1, §4) are kept in the shell's
/// navigation file under the names the file always used, so moving the Calendar into a
/// module of its own changes nothing a device already stored.
/// </summary>
public sealed class ShellCalendarViewPreferencesTests
{
    [Fact]
    public void The_range_and_show_plans_round_trip_under_their_stored_names()
    {
        using var file = new TempShellNavigationFile();
        var preferences = new ShellCalendarViewPreferences(new ShellNavigationStore(file.Path));

        preferences.SetRange(CalendarRange.Week);
        preferences.SetPlansShown(true);

        using (var written = JsonDocument.Parse(ShellNavigationStoreTests.Written(file.Path)))
        {
            Assert.Equal("Week", written.RootElement.GetProperty("calendarRange").GetString());
            Assert.True(written.RootElement.GetProperty("calendarPlansShown").GetBoolean());
        }

        var restarted = new ShellCalendarViewPreferences(new ShellNavigationStore(file.Path));
        Assert.Equal(CalendarRange.Week, restarted.Range);
        Assert.True(restarted.PlansShown);
    }

    [Theory]
    [InlineData("Fortnight")]
    [InlineData("1")]
    [InlineData("")]
    public void A_range_this_build_does_not_define_reads_as_month(string stored)
    {
        using var file = new TempShellNavigationFile();
        File.WriteAllText(file.Path, $$"""{"lastView":"calendar","calendarRange":"{{stored}}"}""");

        var preferences = new ShellCalendarViewPreferences(new ShellNavigationStore(file.Path));

        Assert.Equal(CalendarRange.Month, preferences.Range);
    }

    [Fact]
    public void A_file_written_before_the_calendar_module_reads_back_unchanged()
    {
        const string before =
            """{"lastSurface":"Workspace","lastView":"calendar","calendarRange":"Week","calendarPlansShown":true,"calendarEventsShown":true}""";
        using var file = new TempShellNavigationFile();
        File.WriteAllText(file.Path, before);

        var store = new ShellNavigationStore(file.Path);
        var preferences = new ShellCalendarViewPreferences(store);

        Assert.Equal("calendar", store.LastView);
        Assert.Equal(CalendarRange.Week, preferences.Range);
        Assert.True(preferences.PlansShown);
        Assert.True(preferences.EventsShown);
        // No calendarTasksShown in the file: the tasks are drawn (local ADR 0056 §4).
        Assert.True(preferences.TasksShown);
        Assert.Equal(before, ShellNavigationStoreTests.Written(file.Path));

        // Ticking a box already ticked writes nothing: the file stays the bytes it was.
        preferences.SetEventsShown(true);
        preferences.SetPlansShown(true);
        preferences.SetTasksShown(true);
        Assert.Equal(before, ShellNavigationStoreTests.Written(file.Path));
    }

    /// <summary>Show events is the Calendar's own view choice now, kept under the name the
    /// file always used and apart from Show plans.</summary>
    [Fact]
    public void Show_events_is_off_until_ticked_and_kept_under_its_stored_name_across_a_restart()
    {
        using var file = new TempShellNavigationFile();
        var preferences = new ShellCalendarViewPreferences(new ShellNavigationStore(file.Path));

        Assert.False(preferences.EventsShown);

        preferences.SetEventsShown(true);

        using (var written = JsonDocument.Parse(ShellNavigationStoreTests.Written(file.Path)))
        {
            Assert.True(written.RootElement.GetProperty("calendarEventsShown").GetBoolean());
            Assert.False(written.RootElement.TryGetProperty("calendarPlansShown", out _));
        }

        var restarted = new ShellCalendarViewPreferences(new ShellNavigationStore(file.Path));
        Assert.True(restarted.EventsShown);
        Assert.False(restarted.PlansShown);

        restarted.SetEventsShown(false);
        Assert.DoesNotContain("calendarEventsShown", ShellNavigationStoreTests.Written(file.Path), StringComparison.Ordinal);
        Assert.False(new ShellCalendarViewPreferences(new ShellNavigationStore(file.Path)).EventsShown);
    }
}
