using System.Globalization;
using Backlog.Infrastructure.Sqlite.Changes;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.SharedKernel.Changes;
using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The change journal stays bounded (local ADR 0061 §6): a prune drops the revisions
/// and change sets older than the cutoff and keeps the rest, and a clear drops all.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteChangeHistoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _temp = new("backlog-change-history");

    private CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_prune_deletes_revisions_and_change_sets_older_than_the_cutoff_and_keeps_newer_ones()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        var old = await SaveUnderAsync(repository, "Old");
        var recent = await SaveUnderAsync(repository, "Recent");
        await AgeAsync(repository.DatabasePath, old, Now.AddDays(-91));
        await AgeAsync(repository.DatabasePath, recent, Now.AddDays(-89));

        var purge = await SqliteChangeHistory.PruneAsync(repository.DatabasePath, Now.AddDays(-90), Token);

        Assert.Equal(new ChangeHistoryPurge(1, 1), purge);
        Assert.Equal([recent.ToString()], await IdsAsync(repository.DatabasePath, "SELECT id FROM change_sets;"));
        Assert.Equal([recent.ToString()], await IdsAsync(repository.DatabasePath, "SELECT change_set_id FROM revisions;"));
    }

    [Fact]
    public async Task A_prune_keeps_an_old_change_set_that_still_holds_a_newer_revision()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        var changeSet = await SaveUnderAsync(repository, "Long run");
        await AgeAsync(repository.DatabasePath, changeSet, Now.AddDays(-91), revisionAt: Now.AddDays(-1));

        var purge = await SqliteChangeHistory.PruneAsync(repository.DatabasePath, Now.AddDays(-90), Token);

        Assert.Equal(ChangeHistoryPurge.None, purge);
        Assert.Equal([changeSet.ToString()], await IdsAsync(repository.DatabasePath, "SELECT id FROM change_sets;"));
    }

    /// <summary>A change set goes whole: one revision past the cutoff and one inside
    /// it keep both, so a restore never meets half a change set.</summary>
    [Fact]
    public async Task A_prune_keeps_every_revision_of_a_change_set_whose_newest_is_inside_the_period()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        var changeSet = ChangeSet.Start(ChangeActor.Agent, "long run");
        var first = new TaskItem("First", string.Empty, EntryType.Task);
        using (ChangeContext.Enter(changeSet))
        {
            await repository.SaveAsync(first, Token);
            await repository.SaveAsync(new TaskItem("Second", string.Empty, EntryType.Task), Token);
        }

        await AgeAsync(repository.DatabasePath, changeSet.Id, Now.AddDays(-91), revisionAt: Now.AddDays(-1));
        await ExecuteAsync(
            repository.DatabasePath,
            $"UPDATE revisions SET recorded_at = '{Now.AddDays(-91).ToString("O", CultureInfo.InvariantCulture)}' WHERE document_id = '{first.Id}';");

        var purge = await SqliteChangeHistory.PruneAsync(repository.DatabasePath, Now.AddDays(-90), Token);

        Assert.Equal(ChangeHistoryPurge.None, purge);
        Assert.Equal(2, (await IdsAsync(repository.DatabasePath, "SELECT change_set_id FROM revisions;")).Count);
    }

    /// <summary>The timestamps the triggers write themselves compare with the cutoff
    /// the way the app's do: nothing here rewrites them.</summary>
    [Fact]
    public async Task A_prune_compares_trigger_written_timestamps_with_the_cutoff()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        await SaveUnderAsync(repository, "Just written");

        Assert.Equal(ChangeHistoryPurge.None, await SqliteChangeHistory.PruneAsync(repository.DatabasePath, DateTimeOffset.UtcNow.AddMinutes(-5), Token));

        var purge = await SqliteChangeHistory.PruneAsync(repository.DatabasePath, DateTimeOffset.UtcNow.AddMinutes(5), Token);

        Assert.Equal(new ChangeHistoryPurge(1, 1), purge);
    }

    /// <summary>A clear vacuums, so the file a backup uploads shrinks with it.</summary>
    [Fact]
    public async Task A_clear_shrinks_the_database_file()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        var task = new TaskItem("Large", new string('x', 4000), EntryType.Task);
        for (var i = 0; i < 300; i++)
        {
            using (ChangeContext.Begin(ChangeActor.Agent, $"edit {i}"))
            {
                task.Rename($"Large {i}");
                await repository.SaveAsync(task, Token);
            }
        }

        await ExecuteAsync(repository.DatabasePath, "PRAGMA wal_checkpoint(TRUNCATE);");
        SqliteConnection.ClearAllPools();
        var before = new FileInfo(repository.DatabasePath).Length;

        await SqliteChangeHistory.ClearAsync(repository.DatabasePath, Token);
        SqliteConnection.ClearAllPools();

        var after = new FileInfo(repository.DatabasePath).Length;
        Assert.True(after < before / 4, $"{before} bytes before the clear, {after} after.");
        Assert.Single(await repository.ListAsync(Token));
    }

    [Fact]
    public async Task A_prune_leaves_the_tasks_themselves_alone()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        var changeSet = await SaveUnderAsync(repository, "Kept task");
        await AgeAsync(repository.DatabasePath, changeSet, Now.AddDays(-400));

        await SqliteChangeHistory.PruneAsync(repository.DatabasePath, Now.AddDays(-30), Token);

        Assert.Single(await repository.ListAsync(Token));
    }

    [Fact]
    public async Task A_clear_deletes_every_revision_and_change_set()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        await SaveUnderAsync(repository, "One");
        await SaveUnderAsync(repository, "Two");

        var purge = await SqliteChangeHistory.ClearAsync(repository.DatabasePath, Token);

        Assert.Equal(new ChangeHistoryPurge(2, 2), purge);
        Assert.Empty(await IdsAsync(repository.DatabasePath, "SELECT id FROM change_sets;"));
        Assert.Empty(await IdsAsync(repository.DatabasePath, "SELECT change_set_id FROM revisions;"));
        Assert.Equal(2, (await repository.ListAsync(Token)).Count);
    }

    [Fact]
    public async Task Neither_creates_a_database_that_is_not_there()
    {
        var path = Path.Combine(_temp.Path, "backlog.db");

        Assert.Equal(ChangeHistoryPurge.None, await SqliteChangeHistory.PruneAsync(path, Now, Token));
        Assert.Equal(ChangeHistoryPurge.None, await SqliteChangeHistory.ClearAsync(path, Token));
        Assert.False(File.Exists(path));
    }

    private async Task<Guid> SaveUnderAsync(SqliteTaskRepository repository, string title)
    {
        var changeSet = ChangeSet.Start(ChangeActor.Person, title);
        using (ChangeContext.Enter(changeSet))
        {
            await repository.SaveAsync(new TaskItem(title, string.Empty, EntryType.Task), Token);
        }

        return changeSet.Id;
    }

    /// <summary>Moves a change set's start, and its revisions' record time, back to
    /// <paramref name="at"/> — or the revisions to <paramref name="revisionAt"/>.</summary>
    private async Task AgeAsync(string databasePath, Guid changeSet, DateTimeOffset at, DateTimeOffset? revisionAt = null)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE change_sets SET started_at = $at WHERE id = $id;
            UPDATE revisions SET recorded_at = $revision_at WHERE change_set_id = $id;
            """;
        command.Parameters.AddWithValue("$id", changeSet.ToString());
        command.Parameters.AddWithValue("$at", at.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$revision_at", (revisionAt ?? at).ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(Token);
    }

    private async Task ExecuteAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Token);
    }

    private async Task<List<string>> IdsAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var ids = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token)) ids.Add(reader.GetString(0));
        return ids;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _temp.Dispose();
    }
}
