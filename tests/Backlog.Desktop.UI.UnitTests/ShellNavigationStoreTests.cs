using System.Text.Json;

using Backlog.Infrastructure.FileSystem;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The store decides which surface the shell reopens on. The answer has to
/// survive a restart, and a missing or corrupt file must never stop the app
/// from opening — it just means there is nothing to restore yet.
/// </summary>
public sealed class ShellNavigationStoreTests
{
    [Fact]
    public void Nothing_is_remembered_before_anything_is_set()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);

            Assert.Null(store.LastSurface);
            Assert.Empty(store.LastEnabledPanes);
            Assert.Equal(path, store.SettingsPath);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void The_last_panes_survive_a_restart()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);

            store.SetLastPanes(["Devbook"]);

            var restarted = new ShellNavigationStore(path);

            Assert.Equal(["Devbook"], restarted.LastEnabledPanes);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The header used to hold a pin on each pane, and the file carried the pinned
    /// subset under its own key. Pins are gone — a modifier on the option itself now
    /// says "this one too" — so a file from before still reads, the open panes come
    /// back, and the next save writes it without the key rather than carrying dead
    /// state forward.
    /// </summary>
    [Fact]
    public void A_file_from_before_pins_left_still_reads_and_is_rewritten_without_them()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                {
                  "lastSurface": "Workspace",
                  "lastEnabledPanes": ["Backlog", "Devbook"],
                  "lastPinnedPanes": ["Backlog"]
                }
                """);

            var store = new ShellNavigationStore(path);

            Assert.Equal(["Backlog", "Devbook"], store.LastEnabledPanes);

            store.SetLastPanes(["Devbook"]);

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.False(document.RootElement.TryGetProperty("lastPinnedPanes", out _));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The regression this pins: an earlier version of this store wrote the
    /// surface and the panes through two separate serializations, each holding
    /// only its own field — so setting one silently erased whatever the other
    /// had just remembered. Both have to survive being set in either order.
    /// </summary>
    [Fact]
    public void Setting_the_surface_does_not_erase_the_remembered_panes_or_the_reverse()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);

            store.SetLastPanes(["Devbook"]);
            store.SetLastSurface("Dashboard");

            Assert.Equal("Dashboard", store.LastSurface);
            Assert.Equal(["Devbook"], store.LastEnabledPanes);

            var restarted = new ShellNavigationStore(path);

            Assert.Equal("Dashboard", restarted.LastSurface);
            Assert.Equal(["Devbook"], restarted.LastEnabledPanes);

            restarted.SetLastSurface("Workspace");

            Assert.Equal(["Devbook"], restarted.LastEnabledPanes);

            var restartedAgain = new ShellNavigationStore(path);

            Assert.Equal("Workspace", restartedAgain.LastSurface);
            Assert.Equal(["Devbook"], restartedAgain.LastEnabledPanes);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void Setting_the_same_panes_again_is_a_no_op()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);
            store.SetLastPanes(["Devbook"]);

            var changes = 0;
            store.Changed += () => changes++;
            store.SetLastPanes(["Devbook"]);

            Assert.Equal(0, changes);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void The_last_surface_survives_a_restart()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);

            store.SetLastSurface("Sessions");

            var restarted = new ShellNavigationStore(path);

            Assert.Equal("Sessions", restarted.LastSurface);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void Setting_the_same_surface_again_is_a_no_op()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);
            store.SetLastSurface("Tools");

            var changes = 0;
            store.Changed += () => changes++;
            store.SetLastSurface("Tools");

            Assert.Equal(0, changes);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void Changing_the_surface_says_so()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);
            var changes = 0;
            store.Changed += () => changes++;

            store.SetLastSurface("Dashboard");
            store.SetLastSurface("Workspace");

            Assert.Equal(2, changes);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>A file nobody can read must never stop the app from opening, and
    /// the shell falls back to its own default rather than a stale or broken
    /// surface name.</summary>
    [Fact]
    public void A_corrupt_file_falls_back_to_nothing_remembered()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ this is not json");

            var store = new ShellNavigationStore(path);

            Assert.Null(store.LastSurface);
            Assert.Empty(store.LastEnabledPanes);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void A_missing_file_is_read_back_as_nothing_remembered()
    {
        var path = NewSettingsPath();

        try
        {
            Assert.False(File.Exists(path));
            Assert.Null(new ShellNavigationStore(path).LastSurface);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void The_file_holds_the_surface_and_the_panes_under_their_own_keys()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);
            store.SetLastSurface("Tools");
            store.SetLastPanes(["Backlog", "Devbook"]);

            using var document = JsonDocument.Parse(File.ReadAllText(path));

            Assert.Equal("Tools", document.RootElement.GetProperty("lastSurface").GetString());
            Assert.Equal(
                ["Backlog", "Devbook"],
                document.RootElement.GetProperty("lastEnabledPanes").EnumerateArray().Select(e => e.GetString()));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    private static string NewSettingsPath() =>
        Path.Combine(Path.GetTempPath(), "backlog-shell-navigation-tests", Guid.NewGuid().ToString("n"), "shell-navigation.json");

    private static void DeleteSettingsDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is null) return;

        try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
    }
}
