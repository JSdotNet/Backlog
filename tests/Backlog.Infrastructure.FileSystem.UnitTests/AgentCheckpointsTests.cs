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
    public void A_backlog_with_no_database_yet_still_gets_its_devbook_stores_checkpointed()
    {
        Directory.CreateDirectory(_temp.Combine(DevbookAnnotationStore.FolderName));
        File.WriteAllText(_temp.Combine(DevbookAnnotationStore.FolderName, "repo.json"), "{}");

        Assert.True(Checkpoints().TakeIfDue());

        var folder = Assert.Single(Checkpoints().List()).FolderPath;
        Assert.True(File.Exists(Path.Combine(folder, DevbookAnnotationStore.FolderName, "repo.json")));
    }
}
