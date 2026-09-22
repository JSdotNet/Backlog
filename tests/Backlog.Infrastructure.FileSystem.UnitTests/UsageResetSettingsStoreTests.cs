using Backlog.Infrastructure.FileSystem;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The weekly usage reset on disk, on <see cref="WorkingHoursSettingsStoreTests"/>'
/// terms: nothing until somebody says, what survives a restart, and what clearing
/// leaves behind — no file, so a fresh store answers nothing rather than a stale
/// something.
/// </summary>
public class UsageResetSettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "usage-reset-settings-store-tests-" + Guid.NewGuid().ToString("N"));

    public UsageResetSettingsStoreTests() => Directory.CreateDirectory(_root);

    private string SettingsFile => Path.Combine(_root, "usage-reset.json");

    private UsageResetSettingsStore Store() => new(SettingsFile);

    [Fact]
    public void An_untouched_store_has_no_reset_and_no_file()
    {
        var store = Store();

        Assert.Null(store.Current);
        Assert.False(File.Exists(SettingsFile));
    }

    [Fact]
    public void A_set_reset_survives_a_restart()
    {
        Assert.Null(Store().Set(DayOfWeek.Monday, new TimeOnly(14, 0)));

        var reopened = Store();

        Assert.Equal(new UsageWeekReset(DayOfWeek.Monday, new TimeOnly(14, 0)), reopened.Current);
    }

    [Fact]
    public void Clearing_forgets_the_reset_and_removes_the_file()
    {
        var store = Store();
        _ = store.Set(DayOfWeek.Monday, new TimeOnly(14, 0));

        var raised = 0;
        store.Changed += () => raised++;

        Assert.Null(store.Clear());

        Assert.Null(store.Current);
        Assert.False(File.Exists(SettingsFile));
        Assert.Equal(1, raised);

        // Clearing what is already clear is not a change.
        Assert.Null(store.Clear());
        Assert.Equal(1, raised);
    }

    [Fact]
    public void A_hand_edited_file_that_does_not_parse_reads_as_unset()
    {
        File.WriteAllText(SettingsFile, """{ "day": "Someday", "time": "noon" }""");

        Assert.Null(Store().Current);
    }

    /// <summary>The arithmetic the dashboard cuts weeks with: the most recent reset at
    /// or before now, on the given clock, including the day-of-reset edge in both
    /// directions.</summary>
    [Theory]
    [InlineData("2026-09-23T10:00:00Z", "2026-09-21T12:00:00Z")] // Wednesday: back to Monday 14:00 local (+2)
    [InlineData("2026-09-21T11:59:00Z", "2026-09-14T12:00:00Z")] // Monday 13:59 local: last week's
    [InlineData("2026-09-21T12:00:00Z", "2026-09-21T12:00:00Z")] // Monday 14:00 local exactly: this one
    public void The_most_recent_reset_is_found_on_the_local_clock(string now, string expected)
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-plus-two", TimeSpan.FromHours(2), "+02", "+02");
        var reset = new UsageWeekReset(DayOfWeek.Monday, new TimeOnly(14, 0));

        Assert.Equal(DateTimeOffset.Parse(expected), reset.MostRecentBefore(DateTimeOffset.Parse(now), zone));
    }

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
}
