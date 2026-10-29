using System.Text.Json.Nodes;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// Where each surface's last popped-out window stood (local ADR 0040 §5): kept per
/// surface id in <c>shell-windows.json</c>, written off the caller's thread, read back by
/// the next store, and never in the way of a window opening when the file is missing or
/// corrupt.
/// </summary>
public sealed class ShellWindowStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "shell-window-store-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_root, ShellWindowStore.FileName);

    public void Dispose()
    {
        ShellNavigationStore.WaitForPendingWrites(FilePath);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void The_default_file_sits_beside_the_shell_navigation_file()
    {
        var store = new ShellWindowStore();
        var backlogFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Backlog");

        Assert.Equal(Path.Combine(backlogFolder, "shell-windows.json"), store.SettingsPath);
    }

    [Fact]
    public void Nothing_is_remembered_before_a_window_was_kept()
    {
        var store = new ShellWindowStore(FilePath);

        Assert.Null(store.Get("devbook"));
    }

    [Fact]
    public void Each_surface_keeps_its_own_bounds_across_a_restart()
    {
        var store = new ShellWindowStore(FilePath);
        store.Set("devbook", new ShellWindowBounds(100, 120, 900, 700, Maximized: false));
        store.Set("roadmap", new ShellWindowBounds(-1800, 40, 1600, 1000, Maximized: true));
        store.Flush();

        var reopened = new ShellWindowStore(FilePath);

        Assert.Equal(new ShellWindowBounds(100, 120, 900, 700, false), reopened.Get("devbook"));
        Assert.Equal(new ShellWindowBounds(-1800, 40, 1600, 1000, true), reopened.Get("roadmap"));
        Assert.Null(reopened.Get("tasks"));
    }

    [Fact]
    public void The_last_bounds_set_for_a_surface_are_the_ones_written()
    {
        var store = new ShellWindowStore(FilePath);
        for (var x = 0; x < 50; x++)
        {
            store.Set("devbook", new ShellWindowBounds(x, 10, 800, 600, false));
        }

        store.Flush();

        Assert.Equal(49, new ShellWindowStore(FilePath).Get("devbook")!.X);
    }

    [Fact]
    public void The_file_holds_each_surface_by_its_id_with_its_rectangle_and_maximised_state()
    {
        var store = new ShellWindowStore(FilePath);
        store.Set("devbook", new ShellWindowBounds(10, 20, 800, 600, Maximized: true));
        store.Flush();

        var devbook = JsonNode.Parse(File.ReadAllText(FilePath))!["surfaces"]!["devbook"]!;

        Assert.Equal(10, devbook["x"]!.GetValue<int>());
        Assert.Equal(20, devbook["y"]!.GetValue<int>());
        Assert.Equal(800, devbook["width"]!.GetValue<int>());
        Assert.Equal(600, devbook["height"]!.GetValue<int>());
        Assert.True(devbook["maximized"]!.GetValue<bool>());
    }

    [Fact]
    public void Set_returns_before_the_file_is_written_and_the_value_reads_back_at_once()
    {
        var store = new ShellWindowStore(FilePath);
        var bounds = new ShellWindowBounds(1, 2, 300, 400, false);

        store.Set("devbook", bounds);

        Assert.Equal(bounds, store.Get("devbook"));
    }

    [Fact]
    public void Bounds_with_no_size_are_not_kept()
    {
        var store = new ShellWindowStore(FilePath);

        store.Set("devbook", new ShellWindowBounds(0, 0, 0, 600, false));

        Assert.Null(store.Get("devbook"));
        Assert.False(File.Exists(FilePath));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{ "surfaces": { "devbook": { "x": "left" } } }""")]
    public void A_corrupt_file_reads_as_nothing_remembered(string content)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(FilePath, content);

        var store = new ShellWindowStore(FilePath);

        Assert.Null(store.Get("devbook"));
    }

    [Fact]
    public void A_stored_window_with_no_size_is_skipped_and_the_rest_still_read()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(FilePath, """
            {
              "surfaces": {
                "devbook": { "x": 5, "y": 6, "width": 0, "height": 0 },
                "roadmap": { "x": 5, "y": 6, "width": 700, "height": 500 }
              }
            }
            """);

        var store = new ShellWindowStore(FilePath);

        Assert.Null(store.Get("devbook"));
        Assert.Equal(new ShellWindowBounds(5, 6, 700, 500, false), store.Get("roadmap"));
    }
}
