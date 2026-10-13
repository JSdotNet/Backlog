using System.Globalization;

using Backlog.Infrastructure.Devbook;
using Backlog.Infrastructure.FileSystem;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The Brain setting wired to the Brain's source and to the refresher's folder
/// watch (local ADR 0026 §4): the watch follows the setting — started, re-pointed,
/// stopped — and open panels hear about every move and every rebuilt database.
/// The folder watch itself is the boundary replaced here; the setting, the source
/// and the refresher are the real ones.
/// </summary>
[Collection(BrainRootCollection.Name)]
public sealed class BrainFolderWatchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "brain-folder-watch-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _watched = [];
    private readonly List<string> _stopped = [];
    private readonly BrainSettingsStore _settings;
    private readonly BrainFolderSource _brain;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-13T09:00:00Z", CultureInfo.InvariantCulture));
    private int _changes;

    public BrainFolderWatchTests()
    {
        Directory.CreateDirectory(_root);
        _settings = new BrainSettingsStore(Path.Combine(_root, BrainSettingsStore.FileName));
        _brain = new BrainFolderSource(() => _settings.BrainFolder);
        _brain.Changed += () => Interlocked.Increment(ref _changes);
    }

    private string First => Path.Combine(_root, "OneDrive", "Brain");

    private string Second => Path.Combine(_root, "Dropbox", "Brain");

    private string Cache => Path.Combine(_root, "devbook-cache");

    [Fact]
    public void A_folder_set_before_startup_is_watched_from_startup()
    {
        Set(First);
        using var refresher = Refresher();
        using var watch = Watch(refresher);

        watch.Start();

        Assert.Equal([First], _watched);
    }

    [Fact]
    public void No_folder_set_watches_nothing()
    {
        using var refresher = Refresher();
        using var watch = Watch(refresher);

        watch.Start();

        Assert.Empty(_watched);
    }

    [Fact]
    public void Starting_twice_watches_once()
    {
        Set(First);
        using var refresher = Refresher();
        using var watch = Watch(refresher);

        watch.Start();
        watch.Start();

        Assert.Equal([First], _watched);
    }

    [Fact]
    public void Setting_a_folder_starts_the_watch_and_tells_open_panels()
    {
        using var refresher = Refresher();
        using var watch = Watch(refresher);
        watch.Start();

        Set(First);

        Assert.Equal([First], _watched);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void Moving_the_folder_stops_the_old_watch_and_starts_a_new_one()
    {
        Set(First);
        using var refresher = Refresher();
        using var watch = Watch(refresher);
        watch.Start();

        Set(Second);

        Assert.Equal([First], _stopped);
        Assert.Equal([First, Second], _watched);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void Clearing_the_folder_stops_the_watch_and_starts_none()
    {
        Set(First);
        using var refresher = Refresher();
        using var watch = Watch(refresher);
        watch.Start();

        Set(null);

        Assert.Equal([First], _stopped);
        Assert.Equal([First], _watched);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public async Task A_rebuilt_brain_database_tells_open_panels()
    {
        Directory.CreateDirectory(Path.Combine(First, "projects"));
        await File.WriteAllTextAsync(Path.Combine(First, "projects", "plan.md"), "# Plan\n", TestContext.Current.CancellationToken);
        Set(First);
        using var refresher = Refresher(Cache);
        using var watch = Watch(refresher);

        watch.Start();
        await refresher.Pending(First).WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        Assert.Equal(1, _changes);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(First, "*", SearchOption.AllDirectories),
            file => file.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith("-shm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Disposing_stops_the_watch_and_stops_following_the_setting()
    {
        Set(First);
        using var refresher = Refresher();
        var watch = Watch(refresher);
        watch.Start();

        watch.Dispose();
        Set(Second);

        Assert.Equal([First], _stopped);
        Assert.Equal([First], _watched);
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void A_folder_missing_at_startup_is_watched_once_it_appears()
    {
        Set(First);
        Directory.Delete(First);
        using var refresher = Refresher();
        using var watch = Watch(refresher);
        watch.Start();

        _time.Advance(BrainFolderWatch.DefaultMissingFolderRetry);
        Assert.Equal([First], _watched);
        Assert.Equal(0, _changes);

        Directory.CreateDirectory(First);
        _time.Advance(BrainFolderWatch.DefaultMissingFolderRetry);

        Assert.Equal([First], _stopped);
        Assert.Equal([First, First], _watched);
        Assert.Equal(1, _changes);

        _time.Advance(BrainFolderWatch.DefaultMissingFolderRetry * 3);
        Assert.Equal([First, First], _watched);
    }

    [Fact]
    public void A_failing_panel_neither_fails_the_setting_nor_stops_the_watch_moving()
    {
        Set(First);
        using var refresher = Refresher();
        using var watch = Watch(refresher);
        watch.Start();
        _brain.Changed += () => throw new InvalidOperationException("A pane that broke.");
        var later = 0;
        _settings.Changed += () => later++;

        Set(Second);

        Assert.Equal(Second, watch.WatchedRoot);
        Assert.Equal(1, later);
    }

    [Fact]
    public async Task A_failing_panel_does_not_fail_the_build_that_told_it()
    {
        Directory.CreateDirectory(Path.Combine(First, "projects"));
        await File.WriteAllTextAsync(Path.Combine(First, "projects", "plan.md"), "# Plan\n", TestContext.Current.CancellationToken);
        Set(First);
        using var refresher = Refresher(Cache);
        using var watch = Watch(refresher);
        watch.Start();
        await refresher.Pending(First).WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        _brain.Changed += () => throw new InvalidOperationException("A pane that broke.");

        await File.WriteAllTextAsync(Path.Combine(First, "projects", "next.md"), "# Next\n", TestContext.Current.CancellationToken);

        Assert.True(await refresher.RefreshAsync(First, TestContext.Current.CancellationToken));
    }

    private BrainFolderWatch Watch(DevbookDatabaseRefresher refresher) => new(_settings, _brain, refresher, time: _time);

    private void Set(string? folder) => Assert.Null(_settings.TrySetBrainFolder(folder));

    /// <summary>A refresher whose folder watch is recorded rather than opened, and
    /// which builds nothing unless it is handed a cache folder.</summary>
    private DevbookDatabaseRefresher Refresher(string? cache = null) =>
        new(() => cache, quietInterval: TimeSpan.FromMinutes(5), watchFolder: (root, _) =>
        {
            _watched.Add(root);
            return new Stop(() => _stopped.Add(root));
        });

    private sealed class Stop(Action stopped) : IDisposable
    {
        public void Dispose() => stopped();
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
        catch (UnauthorizedAccessException)
        {
        }
    }
}
