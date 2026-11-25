using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The checkpoint before an agent's write: what goes in it, the thirty-minute rule, keeping
/// the newest ten, and a failed copy that is logged and never thrown.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class AgentCheckpointsTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new("backlog-agent-checkpoints-tests");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingLogger<AgentCheckpoints> _log = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _temp.Dispose();
    }

    private AgentCheckpoints Checkpoints(Action<string, string>? copy = null) =>
        new(() => _temp.Path, _time, _log, copy);

    private async Task SeedAsync()
    {
        await new SqliteTaskRepository(_temp.Path)
            .SaveAsync(new TaskItem("Keep me", string.Empty, EntryType.Task), TestContext.Current.CancellationToken);
        SqliteConnection.ClearAllPools();

        Directory.CreateDirectory(_temp.Combine(DevbookSubjectStateStore.FolderName));
        File.WriteAllText(_temp.Combine(DevbookSubjectStateStore.FolderName, "repo.json"), "{\"subjects\":[]}");
        Directory.CreateDirectory(_temp.Combine(DevbookAnnotationStore.FolderName));
        File.WriteAllText(_temp.Combine(DevbookAnnotationStore.FolderName, "repo.json"), "{\"annotations\":[]}");
    }

    [Fact]
    public async Task A_checkpoint_holds_the_database_and_both_devbook_stores()
    {
        await SeedAsync();

        Assert.True(Checkpoints().TakeIfDue());

        var folder = _temp.Combine(AgentCheckpoints.FolderName, "20261124T090000Z");
        var database = Path.Combine(folder, Path.GetFileName(SqliteTaskRepository.DatabasePathFor(_temp.Path)));
        Assert.True(File.Exists(database));
        Assert.False(SqliteDatabaseFile.IsEmpty(database));
        Assert.True(File.Exists(Path.Combine(folder, DevbookSubjectStateStore.FolderName, "repo.json")));
        Assert.True(File.Exists(Path.Combine(folder, DevbookAnnotationStore.FolderName, "repo.json")));

        var listed = Assert.Single(Checkpoints().List());
        Assert.Equal(_time.GetUtcNow(), listed.TakenAt);
        Assert.True(listed.SizeBytes > 0);
    }

    [Fact]
    public async Task No_second_checkpoint_is_taken_within_thirty_minutes()
    {
        await SeedAsync();
        var checkpoints = Checkpoints();

        Assert.True(checkpoints.TakeIfDue());
        _time.Advance(TimeSpan.FromMinutes(29));
        Assert.False(checkpoints.TakeIfDue());
        Assert.Single(checkpoints.List());

        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(checkpoints.TakeIfDue());
        Assert.Equal(2, checkpoints.List().Count);
    }

    [Fact]
    public async Task The_interval_is_read_from_disk_so_a_restart_owes_nothing()
    {
        await SeedAsync();
        Assert.True(Checkpoints().TakeIfDue());

        _time.Advance(TimeSpan.FromMinutes(10));

        Assert.False(Checkpoints().TakeIfDue());
    }

    [Fact]
    public async Task Only_the_newest_ten_are_kept()
    {
        await SeedAsync();
        var checkpoints = Checkpoints();
        var first = _time.GetUtcNow();
        var foreign = _temp.Combine(AgentCheckpoints.FolderName, "my-notes");
        Directory.CreateDirectory(foreign);

        for (var i = 0; i < 12; i++)
        {
            Assert.True(checkpoints.TakeIfDue());
            _time.Advance(AgentCheckpoints.MinimumInterval);
        }

        var kept = checkpoints.List();
        Assert.Equal(AgentCheckpoints.Keep, kept.Count);
        Assert.Equal(first + (11 * AgentCheckpoints.MinimumInterval), kept[0].TakenAt);
        Assert.Equal(first + (2 * AgentCheckpoints.MinimumInterval), kept[^1].TakenAt);
        Assert.True(Directory.Exists(foreign));
    }

    [Fact]
    public async Task A_failed_copy_is_logged_leaves_nothing_behind_and_is_retried_on_the_next_write()
    {
        await SeedAsync();
        var failing = Checkpoints((_, _) => throw new IOException("The disk is full."));

        Assert.False(failing.TakeIfDue());

        Assert.Empty(failing.List());
        var warning = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.IsType<IOException>(warning.Exception);

        // The failure did not start the interval: the next write tries again at once.
        Assert.True(Checkpoints().TakeIfDue());
    }

    [Fact]
    public async Task A_copy_cut_short_is_never_listed_and_is_cleared_by_the_next_checkpoint()
    {
        await SeedAsync();
        var leftover = _temp.Combine(AgentCheckpoints.FolderName, ".partial-20261124T080000Z");
        Directory.CreateDirectory(leftover);
        File.WriteAllText(Path.Combine(leftover, "backlog.db"), "half");

        Assert.Empty(Checkpoints().List());

        Assert.True(Checkpoints().TakeIfDue());
        Assert.False(Directory.Exists(leftover));
        Assert.Single(Checkpoints().List());
    }

    [Fact]
    public async Task A_checkpoint_stamped_ahead_of_the_clock_does_not_hold_the_next_one_off()
    {
        await SeedAsync();
        Directory.CreateDirectory(_temp.Combine(AgentCheckpoints.FolderName, "20261124T100000Z"));

        Assert.True(Checkpoints().TakeIfDue());
    }

    [Fact]
    public async Task A_listener_that_throws_does_not_undo_or_fail_the_checkpoint()
    {
        await SeedAsync();
        var checkpoints = Checkpoints();
        checkpoints.Changed += () => throw new InvalidOperationException("listener");

        Assert.True(checkpoints.TakeIfDue());
        Assert.Single(checkpoints.List());
    }

    [Fact]
    public void A_checkpoint_holds_the_repository_registry_and_every_settings_file_it_is_given()
    {
        Directory.CreateDirectory(_temp.Combine(GitHubSettingsStore.RegistryFolderName));
        File.WriteAllText(_temp.Combine(GitHubSettingsStore.RegistryFolderName, "repos.json"), "{\"repositories\":[]}");

        var appData = _temp.Combine("app-data-source");
        Directory.CreateDirectory(appData);
        string[] names =
        [
            "github.json",
            "planning-velocity.json",
            MyDayPreferencesStore.FileName,
            IterationSettingsStore.FileName,
            FileMcpGrantStore.FileName,
        ];
        foreach (var name in names) File.WriteAllText(Path.Combine(appData, name), "{\"of\":\"" + name + "\"}");

        var checkpoints = new AgentCheckpoints(
            () => _temp.Path, _time, _log, settingsFiles: () => names.Select(name => Path.Combine(appData, name)));

        Assert.True(checkpoints.TakeIfDue());

        var folder = Assert.Single(checkpoints.List()).FolderPath;
        Assert.Equal(
            "{\"repositories\":[]}",
            File.ReadAllText(Path.Combine(folder, GitHubSettingsStore.RegistryFolderName, "repos.json")));
        foreach (var name in names)
        {
            Assert.Equal(
                "{\"of\":\"" + name + "\"}",
                File.ReadAllText(Path.Combine(folder, AgentCheckpoints.SettingsFolderName, name)));
        }
    }

    [Fact]
    public void A_settings_file_that_is_missing_or_unnamed_is_skipped_and_the_rest_are_still_checkpointed()
    {
        var appData = _temp.Combine("app-data-source");
        Directory.CreateDirectory(appData);
        File.WriteAllText(Path.Combine(appData, MyDayPreferencesStore.FileName), "{}");

        var checkpoints = new AgentCheckpoints(
            () => _temp.Path,
            _time,
            _log,
            settingsFiles: () =>
            [
                Path.Combine(appData, FileMcpGrantStore.FileName),
                null,
                "  ",
                Path.Combine(appData, MyDayPreferencesStore.FileName),
            ]);

        Assert.True(checkpoints.TakeIfDue());

        var folder = Assert.Single(checkpoints.List()).FolderPath;
        Assert.True(File.Exists(Path.Combine(folder, AgentCheckpoints.SettingsFolderName, MyDayPreferencesStore.FileName)));
        Assert.False(File.Exists(Path.Combine(folder, AgentCheckpoints.SettingsFolderName, FileMcpGrantStore.FileName)));
        Assert.False(Directory.Exists(Path.Combine(folder, GitHubSettingsStore.RegistryFolderName)));
        Assert.Empty(_log.Entries);
        Assert.Empty(Directory.EnumerateDirectories(_temp.Combine(AgentCheckpoints.FolderName), ".partial-*"));
    }

    [Fact]
    public void Two_settings_files_of_one_name_keep_the_first_rather_than_failing_the_checkpoint()
    {
        var first = _temp.Combine("first");
        var second = _temp.Combine("second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first, "github.json"), "first");
        File.WriteAllText(Path.Combine(second, "github.json"), "second");

        var checkpoints = new AgentCheckpoints(
            () => _temp.Path, _time, _log,
            settingsFiles: () => [Path.Combine(first, "github.json"), Path.Combine(second, "github.json")]);

        Assert.True(checkpoints.TakeIfDue());

        var folder = Assert.Single(checkpoints.List()).FolderPath;
        Assert.Equal("first", File.ReadAllText(Path.Combine(folder, AgentCheckpoints.SettingsFolderName, "github.json")));
    }

    [Fact]
    public void A_settings_file_list_that_throws_fails_the_checkpoint_without_leaving_a_partial_folder()
    {
        var checkpoints = new AgentCheckpoints(
            () => _temp.Path, _time, _log,
            settingsFiles: () => throw new InvalidOperationException("no store"));

        Assert.False(checkpoints.TakeIfDue());

        Assert.Empty(checkpoints.List());
        Assert.Single(_log.Entries);
        Assert.False(Directory.Exists(_temp.Combine(AgentCheckpoints.FolderName)));
    }

    [Fact]
    public void A_settings_file_that_cannot_be_read_is_left_out_and_logged_and_the_checkpoint_is_still_taken()
    {
        var appData = _temp.Combine("app-data-source");
        Directory.CreateDirectory(appData);
        var locked = Path.Combine(appData, FileMcpGrantStore.FileName);
        var readable = Path.Combine(appData, MyDayPreferencesStore.FileName);
        File.WriteAllText(locked, "{}");
        File.WriteAllText(readable, "{}");

        var checkpoints = new AgentCheckpoints(() => _temp.Path, _time, _log, settingsFiles: () => [locked, readable]);

        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.True(checkpoints.TakeIfDue());
        }

        var folder = Assert.Single(checkpoints.List()).FolderPath;
        Assert.True(File.Exists(Path.Combine(folder, AgentCheckpoints.SettingsFolderName, MyDayPreferencesStore.FileName)));
        Assert.False(File.Exists(Path.Combine(folder, AgentCheckpoints.SettingsFolderName, FileMcpGrantStore.FileName)));
        Assert.Contains(locked, Assert.Single(_log.Entries).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_backlog_with_no_database_yet_still_gets_its_devbook_stores_checkpointed()
    {
        Directory.CreateDirectory(_temp.Combine(DevbookAnnotationStore.FolderName));
        File.WriteAllText(_temp.Combine(DevbookAnnotationStore.FolderName, "repo.json"), "{}");

        Assert.True(Checkpoints().TakeIfDue());

        var folder = Assert.Single(Checkpoints().List()).FolderPath;
        Assert.True(File.Exists(Path.Combine(folder, DevbookAnnotationStore.FolderName, "repo.json")));
    }
}
