using System.Text.Json.Nodes;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// Where each surface's last popped-out window stood (local ADR 0043 §5): kept per
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

        Assert.Equal(
            Path.Combine(Path.GetDirectoryName(new ShellNavigationStore().SettingsPath)!, "shell-windows.json"),
            store.SettingsPath);
    }

    /// <summary>
    /// Both shell stores live in the head's own app-data folder: <c>Backlog.Debug</c> for
    /// a Debug build, the folder the log and the workspace already use there, so a head
    /// started out of a checkout never overwrites the installed app's
    /// <c>shell-navigation.json</c> and <c>shell-windows.json</c>. A Debug composition
    /// names the installed app's <c>Backlog</c> folder for neither.
    /// </summary>
    [Fact]
    public void The_shell_stores_default_to_the_heads_own_app_data_folder()
    {
        var appData = WorkspaceSettingsStore.DefaultAppDataDirectory;
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Backlog");

        string[] paths = [new ShellNavigationStore().SettingsPath, new ShellWindowStore().SettingsPath];

        Assert.Equal(
            [Path.Combine(appData, ShellNavigationStore.FileName), Path.Combine(appData, ShellWindowStore.FileName)],
            paths);
#if DEBUG
        Assert.Equal("Backlog.Debug", WorkspaceSettingsStore.DefaultAppDataFolderName);
        Assert.All(paths, path => Assert.NotEqual(installed, Path.GetDirectoryName(path), StringComparer.OrdinalIgnoreCase));
#else
        Assert.All(paths, path => Assert.Equal(installed, Path.GetDirectoryName(path), StringComparer.OrdinalIgnoreCase));
#endif
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

    [Fact]
    public void No_window_is_remembered_until_one_is_remembered()
    {
        var store = new ShellWindowStore(FilePath);
        store.Set("devbook", new ShellWindowBounds(1, 2, 300, 400, false));

        Assert.Empty(store.Remembered);
        Assert.Null(store.GetRemembered("window-1"));
    }

    [Fact]
    public void A_remembered_window_reads_back_with_its_surface_query_and_bounds_after_a_restart()
    {
        var store = new ShellWindowStore(FilePath);
        store.Remember(new RememberedShellWindow(
            "window-1",
            "devbook",
            new Dictionary<string, string> { ["chapter"] = ".devbook/arc42/adr/0043.md#5", ["scope"] = "backlog" },
            new ShellWindowBounds(-1800, 40, 1600, 1000, Maximized: true)));
        store.Flush();

        var remembered = Assert.Single(new ShellWindowStore(FilePath).Remembered);

        Assert.Equal("window-1", remembered.Key);
        Assert.Equal("devbook", remembered.SurfaceId);
        Assert.Equal(".devbook/arc42/adr/0043.md#5", remembered.Query["chapter"]);
        Assert.Equal("backlog", remembered.Query["scope"]);
        Assert.Equal(2, remembered.Query.Count);
        Assert.Equal(new ShellWindowBounds(-1800, 40, 1600, 1000, true), remembered.Bounds);
    }

    [Fact]
    public void A_remembered_window_with_no_bounds_keeps_its_surface_and_query()
    {
        var store = new ShellWindowStore(FilePath);
        store.Remember(new RememberedShellWindow("window-1", "roadmap", new Dictionary<string, string>(), Bounds: null));
        store.Flush();

        var remembered = Assert.Single(new ShellWindowStore(FilePath).Remembered);

        Assert.Equal("roadmap", remembered.SurfaceId);
        Assert.Empty(remembered.Query);
        Assert.Null(remembered.Bounds);
    }

    [Fact]
    public void Remembering_a_window_again_replaces_its_entry_and_keeps_its_place_in_the_order()
    {
        var store = new ShellWindowStore(FilePath);
        store.Remember(new RememberedShellWindow("first", "devbook", new Dictionary<string, string>(), null));
        store.Remember(new RememberedShellWindow("second", "roadmap", new Dictionary<string, string>(), null));
        store.Remember(new RememberedShellWindow("first", "alpha", new Dictionary<string, string> { ["note"] = "7" }, new ShellWindowBounds(1, 2, 300, 400, false)));
        store.Flush();

        var reopened = new ShellWindowStore(FilePath);

        Assert.Equal(["first", "second"], reopened.Remembered.Select(window => window.Key));
        Assert.Equal("alpha", reopened.GetRemembered("first")!.SurfaceId);
        Assert.Equal("7", reopened.GetRemembered("first")!.Query["note"]);
        Assert.Equal(new ShellWindowBounds(1, 2, 300, 400, false), reopened.GetRemembered("first")!.Bounds);
    }

    [Fact]
    public void A_forgotten_window_is_no_longer_remembered_after_a_restart()
    {
        var store = new ShellWindowStore(FilePath);
        store.Remember(new RememberedShellWindow("first", "devbook", new Dictionary<string, string>(), null));
        store.Remember(new RememberedShellWindow("second", "roadmap", new Dictionary<string, string>(), null));

        store.Forget("first");
        store.Flush();

        Assert.Null(store.GetRemembered("first"));
        Assert.Equal(["second"], new ShellWindowStore(FilePath).Remembered.Select(window => window.Key));
    }

    /// <summary>The remembered windows sit beside the per-surface bounds, which keep
    /// their stored name: a file an earlier build wrote still loads.</summary>
    [Fact]
    public void Remembered_windows_get_a_section_of_their_own_and_surfaces_keeps_its_name()
    {
        var store = new ShellWindowStore(FilePath);
        store.Set("devbook", new ShellWindowBounds(10, 20, 800, 600, Maximized: false));
        store.Remember(new RememberedShellWindow("window-1", "roadmap", new Dictionary<string, string> { ["scope"] = "brain" }, new ShellWindowBounds(5, 6, 700, 500, false)));
        store.Flush();

        var file = JsonNode.Parse(File.ReadAllText(FilePath))!;
        var entry = file["remembered"]!.AsArray().Single()!;

        Assert.Equal(800, file["surfaces"]!["devbook"]!["width"]!.GetValue<int>());
        Assert.Equal("window-1", entry["key"]!.GetValue<string>());
        Assert.Equal("roadmap", entry["surfaceId"]!.GetValue<string>());
        Assert.Equal("brain", entry["query"]!["scope"]!.GetValue<string>());
        Assert.Equal(700, entry["bounds"]!["width"]!.GetValue<int>());
    }

    [Fact]
    public void A_file_from_before_remembered_windows_loads_its_bounds_and_remembers_no_window()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(FilePath, """
            { "surfaces": { "roadmap": { "x": 5, "y": 6, "width": 700, "height": 500, "maximized": false } } }
            """);

        var store = new ShellWindowStore(FilePath);

        Assert.Empty(store.Remembered);
        Assert.Equal(new ShellWindowBounds(5, 6, 700, 500, false), store.Get("roadmap"));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{ "remembered": { "key": "not a list" } }""")]
    [InlineData("""{ "remembered": [ { "key": "window-1", "surfaceId": "devbook", "bounds": { "x": "left" } } ] }""")]
    public void A_corrupt_file_remembers_no_window(string content)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(FilePath, content);

        Assert.Empty(new ShellWindowStore(FilePath).Remembered);
    }

    /// <summary>A query value stored as null could never be put in a window's address,
    /// so it is left out and the window still reads, as do the windows after it.</summary>
    [Fact]
    public void A_stored_query_value_of_null_is_left_out_and_every_window_still_reads()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(FilePath, """
            {
              "remembered": [
                { "key": "a", "surfaceId": "devbook", "query": { "chapter": null, "scope": "docs" } },
                { "key": "b", "surfaceId": "roadmap" }
              ]
            }
            """);

        var remembered = new ShellWindowStore(FilePath).Remembered;

        Assert.Equal(["a", "b"], remembered.Select(window => window.Key));
        Assert.Equal(new Dictionary<string, string> { ["scope"] = "docs" }, remembered[0].Query);
    }

    [Fact]
    public void A_stored_window_with_no_key_or_surface_is_skipped_and_the_rest_still_read()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(FilePath, """
            {
              "remembered": [
                { "surfaceId": "devbook" },
                { "key": "no-surface" },
                null,
                { "key": "window-1", "surfaceId": "roadmap", "bounds": { "x": 1, "y": 2, "width": 0, "height": 0 } }
              ]
            }
            """);

        var remembered = Assert.Single(new ShellWindowStore(FilePath).Remembered);

        Assert.Equal("window-1", remembered.Key);
        Assert.Null(remembered.Bounds);
    }

    /// <summary>Kept on top and remembered windows are sections of their own beside
    /// <c>surfaces</c>: setting one leaves the others as they were, across a restart.</summary>
    [Fact]
    public void Kept_on_top_and_remembered_windows_sit_beside_the_surfaces_and_survive_a_restart_together()
    {
        var store = new ShellWindowStore(FilePath);
        store.Set("devbook", new ShellWindowBounds(10, 20, 800, 600, Maximized: false));
        store.SetOnTop("tasks", true);
        store.Remember(new RememberedShellWindow("window-1", "roadmap", new Dictionary<string, string>(), null));
        store.Flush();

        var file = JsonNode.Parse(File.ReadAllText(FilePath))!;
        Assert.Equal(800, file["surfaces"]!["devbook"]!["width"]!.GetValue<int>());
        Assert.Equal(["tasks"], file["onTop"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Equal("window-1", file["remembered"]!.AsArray().Single()!["key"]!.GetValue<string>());

        var reopened = new ShellWindowStore(FilePath);
        Assert.True(reopened.IsOnTop("tasks"));
        Assert.Equal("roadmap", Assert.Single(reopened.Remembered).SurfaceId);
        Assert.Equal(new ShellWindowBounds(10, 20, 800, 600, false), reopened.Get("devbook"));

        reopened.SetOnTop("tasks", false);
        reopened.Forget("window-1");
        reopened.Flush();

        var emptied = JsonNode.Parse(File.ReadAllText(FilePath))!;
        Assert.Null(emptied["onTop"]);
        Assert.Null(emptied["remembered"]);
        Assert.NotNull(emptied["surfaces"]);
    }

    [Fact]
    public void No_surface_is_kept_on_top_before_one_was_pinned()
    {
        var store = new ShellWindowStore(FilePath);

        Assert.False(store.IsOnTop("tasks"));
    }

    [Fact]
    public void A_surface_pinned_on_top_stays_pinned_across_a_restart_and_unpins()
    {
        var store = new ShellWindowStore(FilePath);
        store.SetOnTop("tasks", true);
        store.Flush();

        var reopened = new ShellWindowStore(FilePath);
        Assert.True(reopened.IsOnTop("tasks"));
        Assert.False(reopened.IsOnTop("devbook"));

        reopened.SetOnTop("tasks", false);
        reopened.Flush();

        Assert.False(new ShellWindowStore(FilePath).IsOnTop("tasks"));
    }

    /// <summary>The pin is kept apart from the bounds: a window that moves after it was
    /// pinned writes its new rectangle and keeps the pin, and a pin needs no rectangle.</summary>
    [Fact]
    public void The_pin_survives_the_bounds_being_rewritten()
    {
        var store = new ShellWindowStore(FilePath);
        store.SetOnTop("tasks", true);
        store.Set("tasks", new ShellWindowBounds(10, 20, 420, 1300, Maximized: false));
        store.Flush();

        var reopened = new ShellWindowStore(FilePath);

        Assert.True(reopened.IsOnTop("tasks"));
        Assert.Equal(new ShellWindowBounds(10, 20, 420, 1300, false), reopened.Get("tasks"));
    }

    [Fact]
    public void The_file_lists_the_pinned_surfaces_by_id()
    {
        var store = new ShellWindowStore(FilePath);
        store.SetOnTop("tasks", true);
        store.Flush();

        var json = JsonNode.Parse(File.ReadAllText(FilePath))!;

        Assert.Equal(["tasks"], json["onTop"]!.AsArray().Select(node => node!.GetValue<string>()));
    }
}
