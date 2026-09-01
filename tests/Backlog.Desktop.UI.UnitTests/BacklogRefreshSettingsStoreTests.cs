using System.Text.Json;

using Backlog.Modules.Backlog.Abstractions.Services;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The store decides whether the list notices work done on another machine, and
/// how quickly. Both answers have to survive a restart, and a number typed into
/// the field has to be refused rather than turned into a spin.
/// </summary>
public sealed class BacklogRefreshSettingsStoreTests
{
    [Fact]
    public void Polling_is_on_at_a_sensible_interval_before_anybody_chooses()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new BacklogRefreshSettingsStore(path);

            Assert.True(store.Current.PollingEnabled);
            Assert.Equal(BacklogRefreshSettings.DefaultPollingIntervalSeconds, store.Current.PollingIntervalSeconds);
            Assert.Equal(path, store.SettingsPath);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void Both_choices_survive_a_restart()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new BacklogRefreshSettingsStore(path);

            Assert.Null(store.SetPollingIntervalSeconds(30));
            Assert.Null(store.SetPollingEnabled(false));

            var restarted = new BacklogRefreshSettingsStore(path);

            Assert.False(restarted.Current.PollingEnabled);
            Assert.Equal(30, restarted.Current.PollingIntervalSeconds);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>Turning the check off is not the same as forgetting how often it
    /// ran: switching it back on has to return the interval that was chosen.</summary>
    [Fact]
    public void Turning_the_check_off_keeps_the_interval()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new BacklogRefreshSettingsStore(path);
            store.SetPollingIntervalSeconds(12);
            store.SetPollingEnabled(false);
            store.SetPollingEnabled(true);

            Assert.True(store.Current.PollingEnabled);
            Assert.Equal(12, store.Current.PollingIntervalSeconds);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void An_interval_below_a_second_is_refused_rather_than_saved(int seconds)
    {
        var path = NewSettingsPath();

        try
        {
            var store = new BacklogRefreshSettingsStore(path);

            var message = store.SetPollingIntervalSeconds(seconds);

            Assert.NotNull(message);
            Assert.Equal(BacklogRefreshSettings.DefaultPollingIntervalSeconds, store.Current.PollingIntervalSeconds);
            Assert.Equal(
                BacklogRefreshSettings.DefaultPollingIntervalSeconds,
                new BacklogRefreshSettingsStore(path).Current.PollingIntervalSeconds);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void The_shortest_allowed_interval_is_accepted()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new BacklogRefreshSettingsStore(path);

            Assert.Null(store.SetPollingIntervalSeconds(BacklogRefreshSettings.MinimumPollingIntervalSeconds));

            Assert.Equal(
                BacklogRefreshSettings.MinimumPollingIntervalSeconds,
                store.Current.PollingIntervalSeconds);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void Changing_either_setting_says_so()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new BacklogRefreshSettingsStore(path);
            var changes = 0;
            store.Changed += () => changes++;

            store.SetPollingEnabled(false);
            store.SetPollingIntervalSeconds(20);

            Assert.Equal(2, changes);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>A refusal is not a change: nothing was written, so nothing that
    /// reads the setting has anything to react to.</summary>
    [Fact]
    public void A_refused_interval_says_nothing_changed()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new BacklogRefreshSettingsStore(path);
            var changes = 0;
            store.Changed += () => changes++;

            store.SetPollingIntervalSeconds(0);

            Assert.Equal(0, changes);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>A file nobody can read must never stop the app from opening, and
    /// a value it does not mention is the default rather than zero.</summary>
    [Fact]
    public void A_corrupt_or_partial_file_falls_back_to_the_defaults()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ this is not json");

            Assert.True(new BacklogRefreshSettingsStore(path).Current.PollingEnabled);

            File.WriteAllText(path, JsonSerializer.Serialize(new { pollingEnabled = false }));

            var partial = new BacklogRefreshSettingsStore(path);

            Assert.False(partial.Current.PollingEnabled);
            Assert.Equal(BacklogRefreshSettings.DefaultPollingIntervalSeconds, partial.Current.PollingIntervalSeconds);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>An interval written by hand, or by a build that allowed one, is
    /// pulled up to the floor rather than honoured.</summary>
    [Fact]
    public void An_impossible_interval_in_the_file_is_read_back_as_the_floor()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { pollingEnabled = true, pollingIntervalSeconds = 0 }));

            Assert.Equal(
                BacklogRefreshSettings.MinimumPollingIntervalSeconds,
                new BacklogRefreshSettingsStore(path).Current.PollingIntervalSeconds);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    private static string NewSettingsPath() =>
        Path.Combine(Path.GetTempPath(), "backlog-refresh-tests", Guid.NewGuid().ToString("n"), "refresh.json");

    private static void DeleteSettingsDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is null) return;

        try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
    }
}
