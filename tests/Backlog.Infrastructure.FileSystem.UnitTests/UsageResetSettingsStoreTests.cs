using Backlog.Infrastructure.FileSystem;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The named weekly usage resets on disk, on <see cref="WorkingHoursSettingsStoreTests"/>'
/// terms: nothing until somebody says, what survives a restart, what a file written
/// before there could be more than one reset reads as, and what removing the last one
/// leaves behind — no file, so a fresh store answers nothing rather than a stale
/// something.
/// </summary>
public class UsageResetSettingsStoreTests : IDisposable
{
    private static readonly TimeOnly TwoPm = new(14, 0);

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

        Assert.Empty(store.Resets);
        Assert.Null(store.Selected);
        Assert.Null(store.Current);
        Assert.False(File.Exists(SettingsFile));
    }

    [Fact]
    public void Added_resets_survive_a_restart_in_the_order_they_were_added()
    {
        var store = Store();

        var (work, workError) = store.Add("Work", DayOfWeek.Monday, TwoPm);
        var (personal, personalError) = store.Add("Personal", DayOfWeek.Thursday, new TimeOnly(9, 30));

        Assert.Null(workError);
        Assert.Null(personalError);
        Assert.NotNull(work);
        Assert.NotNull(personal);
        Assert.NotEqual(work, personal);

        var reopened = Store();

        Assert.Equal(
            [
                new NamedUsageReset(work!, "Work", new UsageWeekReset(DayOfWeek.Monday, TwoPm)),
                new NamedUsageReset(personal!, "Personal", new UsageWeekReset(DayOfWeek.Thursday, new TimeOnly(9, 30)))
            ],
            reopened.Resets);

        // Nothing chosen yet: the first one cuts the weeks.
        Assert.Equal(work, reopened.Selected?.Id);
        Assert.Equal(new UsageWeekReset(DayOfWeek.Monday, TwoPm), reopened.Current);
    }

    [Fact]
    public void An_update_survives_a_restart_and_keeps_the_id()
    {
        var store = Store();
        var (id, _) = store.Add("Work", DayOfWeek.Monday, TwoPm);

        Assert.Null(store.Update(id!, "  Day job  ", DayOfWeek.Tuesday, new TimeOnly(21, 0)));

        Assert.Equal(
            new NamedUsageReset(id!, "Day job", new UsageWeekReset(DayOfWeek.Tuesday, new TimeOnly(21, 0))),
            Assert.Single(Store().Resets));
    }

    [Fact]
    public void An_update_that_changes_nothing_raises_nothing()
    {
        var store = Store();
        var (id, _) = store.Add("Work", DayOfWeek.Monday, TwoPm);

        var raised = 0;
        store.Changed += () => raised++;

        Assert.Null(store.Update(id!, "Work", DayOfWeek.Monday, TwoPm));
        Assert.Equal(0, raised);
    }

    [Fact]
    public void A_choice_survives_a_restart()
    {
        var store = Store();
        _ = store.Add("Work", DayOfWeek.Monday, TwoPm);
        var (personal, _) = store.Add("Personal", DayOfWeek.Thursday, TwoPm);

        Assert.Null(store.Select(personal!));

        var reopened = Store();

        Assert.Equal(personal, reopened.Selected?.Id);
        Assert.Equal(new UsageWeekReset(DayOfWeek.Thursday, TwoPm), reopened.Current);
    }

    [Fact]
    public void Choosing_a_reset_that_is_not_there_is_refused_and_the_choice_stays()
    {
        var store = Store();
        var (work, _) = store.Add("Work", DayOfWeek.Monday, TwoPm);

        Assert.NotNull(store.Select("gone"));
        Assert.Equal(work, store.Selected?.Id);
    }

    [Fact]
    public void Removing_the_chosen_reset_hands_the_cut_to_the_first_remaining_one()
    {
        var store = Store();
        var (work, _) = store.Add("Work", DayOfWeek.Monday, TwoPm);
        var (personal, _) = store.Add("Personal", DayOfWeek.Thursday, TwoPm);
        var (side, _) = store.Add("Side", DayOfWeek.Friday, TwoPm);
        _ = store.Select(personal!);

        Assert.Null(store.Remove(personal!));

        Assert.Equal([work, side], store.Resets.Select(reset => reset.Id));
        Assert.Equal(work, store.Selected?.Id);
        Assert.Equal(work, Store().Selected?.Id);
    }

    [Fact]
    public void Removing_the_last_reset_removes_the_file()
    {
        var store = Store();
        var (id, _) = store.Add("Work", DayOfWeek.Monday, TwoPm);

        var raised = 0;
        store.Changed += () => raised++;

        Assert.Null(store.Remove(id!));

        Assert.Empty(store.Resets);
        Assert.Null(store.Current);
        Assert.False(File.Exists(SettingsFile));
        Assert.Equal(1, raised);

        // Removing what is already gone is not a change.
        Assert.Null(store.Remove(id!));
        Assert.Equal(1, raised);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("work")]
    [InlineData(" WORK ")]
    [InlineData("A name that runs on for a good deal past forty characters")]
    public void A_blank_duplicate_or_overlong_name_is_refused_and_the_state_stays(string name)
    {
        var store = Store();
        var (work, _) = store.Add("Work", DayOfWeek.Monday, TwoPm);

        var raised = 0;
        store.Changed += () => raised++;

        var (id, error) = store.Add(name, DayOfWeek.Friday, TwoPm);

        Assert.Null(id);
        Assert.NotNull(error);
        Assert.Equal(0, raised);
        Assert.Single(store.Resets);
        Assert.Single(Store().Resets);

        // Its own name is not a duplicate of itself, so an update keeping it goes through.
        Assert.Null(store.Update(work!, "WORK", DayOfWeek.Monday, TwoPm));
    }

    [Fact]
    public void Renaming_to_another_resets_name_is_refused_and_the_state_stays()
    {
        var store = Store();
        _ = store.Add("Work", DayOfWeek.Monday, TwoPm);
        var (personal, _) = store.Add("Personal", DayOfWeek.Thursday, TwoPm);

        Assert.NotNull(store.Update(personal!, "work", DayOfWeek.Friday, TwoPm));

        Assert.Equal(
            new NamedUsageReset(personal!, "Personal", new UsageWeekReset(DayOfWeek.Thursday, TwoPm)),
            store.Resets[1]);
    }

    [Fact]
    public void Updating_a_reset_that_is_not_there_is_refused()
    {
        var store = Store();

        Assert.NotNull(store.Update("gone", "Work", DayOfWeek.Monday, TwoPm));
        Assert.Empty(store.Resets);
    }

    /// <summary>The file a single-reset version wrote: one reset, selected, under a
    /// name the person can change — and the file left as it was until they do.</summary>
    [Fact]
    public void A_file_from_before_named_resets_reads_as_one_chosen_reset_and_is_not_rewritten()
    {
        const string legacy = """{ "day": "Monday", "time": "14:00" }""";
        File.WriteAllText(SettingsFile, legacy);

        var store = Store();

        Assert.Equal(
            new NamedUsageReset("default", "Weekly reset", new UsageWeekReset(DayOfWeek.Monday, TwoPm)),
            Assert.Single(store.Resets));
        Assert.Equal("default", store.Selected?.Id);
        Assert.Equal(new UsageWeekReset(DayOfWeek.Monday, TwoPm), store.Current);
        Assert.Equal(legacy, File.ReadAllText(SettingsFile));
    }

    [Fact]
    public void The_first_change_to_a_file_from_before_named_resets_writes_the_new_shape()
    {
        File.WriteAllText(SettingsFile, """{ "day": "Monday", "time": "14:00" }""");

        var (work, _) = Store().Add("Work", DayOfWeek.Thursday, TwoPm);

        Assert.Contains("\"resets\"", File.ReadAllText(SettingsFile), StringComparison.Ordinal);

        var reopened = Store();

        Assert.Equal(["default", work], reopened.Resets.Select(reset => reset.Id));
        Assert.Equal("default", reopened.Selected?.Id);
    }

    [Fact]
    public void A_hand_edited_file_that_does_not_parse_reads_as_unset()
    {
        File.WriteAllText(SettingsFile, """{ "day": "Someday", "time": "noon" }""");

        Assert.Empty(Store().Resets);
    }

    [Fact]
    public void A_file_that_is_not_json_reads_as_unset()
    {
        File.WriteAllText(SettingsFile, "not json {");

        Assert.Null(Store().Selected);
    }

    [Fact]
    public void An_entry_that_does_not_parse_is_skipped_and_its_siblings_are_kept()
    {
        File.WriteAllText(SettingsFile, """
            {
              "resets": [
                { "id": "a", "name": "Work", "day": "Monday", "time": "14:00" },
                { "id": "b", "name": "Broken", "day": "Someday", "time": "14:00" },
                { "id": "c", "name": "Late", "day": "Friday", "time": "noon" },
                { "id": "a", "name": "Same id", "day": "Tuesday", "time": "10:00" },
                { "id": "d", "name": "Personal", "day": "Thursday", "time": "09:30" }
              ],
              "selected": "b"
            }
            """);

        var store = Store();

        Assert.Equal(["a", "d"], store.Resets.Select(reset => reset.Id));

        // The stored choice named a skipped entry, so the first one cuts the weeks.
        Assert.Equal("a", store.Selected?.Id);
    }

    [Fact]
    public void A_change_that_cannot_be_saved_says_so_and_still_takes_effect()
    {
        var store = Store();

        var raised = 0;
        store.Changed += () => raised++;

        // A folder where the file should be: the write fails with an IO error.
        Directory.CreateDirectory(SettingsFile);

        var (id, error) = store.Add("Work", DayOfWeek.Monday, TwoPm);

        Assert.NotNull(id);
        Assert.Equal("Changed, but the weekly reset couldn't be saved for next time.", error);
        Assert.Single(store.Resets);
        Assert.Equal(1, raised);
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
