using System.Text.Json;

using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.FileSystem;
using Backlog.UI.Components.Tasks;

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
        Assert.True(store.CalendarEventsShown);
        Assert.Equal(before, ShellNavigationStoreTests.Written(file.Path));
    }
}
