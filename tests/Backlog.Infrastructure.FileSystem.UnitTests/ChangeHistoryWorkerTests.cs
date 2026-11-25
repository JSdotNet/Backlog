using System.Globalization;
using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.SharedKernel.Changes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The change journal's retention (local ADR 0061 §6): 90 days unless the person picks
/// 30 or 365, a daily prune by that period driven by the clock, and a clear that
/// takes everything.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class ChangeHistoryWorkerTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new("backlog-change-history-worker-tests");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero));
    private readonly WorkspaceSettingsStore _settings;

    public ChangeHistoryWorkerTests()
    {
        var root = _temp.Combine("data");
        _settings = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"), _ => null);
    }

    private CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _temp.Dispose();
    }

    [Fact]
    public void The_period_is_90_days_until_somebody_picks_30_or_365_and_survives_a_restart()
    {
        Assert.Equal(90, _settings.ChangeHistoryDays);
        Assert.Equal([30, 90, 365], ChangeHistoryRetention.Offered);

        Assert.NotNull(_settings.SetChangeHistoryDays(45));
        Assert.Equal(90, _settings.ChangeHistoryDays);

        Assert.Null(_settings.SetChangeHistoryDays(365));

        var root = _temp.Combine("data");
        var reopened = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"), _ => null);
        Assert.Equal(365, reopened.ChangeHistoryDays);
    }

    [Fact]
    public async Task The_daily_tick_deletes_history_older_than_the_chosen_period()
    {
        Assert.Null(_settings.SetChangeHistoryDays(30));
        await SeedAsync("Old", _time.GetUtcNow().AddDays(-31));
        var recent = await SeedAsync("Recent", _time.GetUtcNow().AddDays(-29));

        using var worker = new ChangeHistoryWorker(_settings, _time);
        await TickAsync(worker, ChangeHistoryWorker.StartDelay);

        Assert.Equal([recent], await ChangeSetIdsAsync());

        // A day on, the one that was 29 days old is 30 and a bit: the next tick takes it.
        await TickAsync(worker, ChangeHistoryWorker.Interval);

        Assert.Empty(await ChangeSetIdsAsync());
    }

    [Fact]
    public async Task With_the_default_period_a_60_day_old_change_set_is_kept()
    {
        var kept = await SeedAsync("Kept", _time.GetUtcNow().AddDays(-60));

        using var worker = new ChangeHistoryWorker(_settings, _time);
        await worker.PruneAsync(Token);

        Assert.Equal([kept], await ChangeSetIdsAsync());
    }

    [Fact]
    public async Task Clearing_deletes_every_revision_and_change_set_and_keeps_the_tasks()
    {
        await SeedAsync("One", _time.GetUtcNow());
        await SeedAsync("Two", _time.GetUtcNow().AddDays(-5));

        using var worker = new ChangeHistoryWorker(_settings, _time);
        var changed = 0;
        worker.Changed += () => changed++;

        Assert.Null(await worker.ClearAsync(Token));

        Assert.Empty(await ChangeSetIdsAsync());
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM revisions;"));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM tasks;"));
        Assert.Equal(1, changed);
    }

    [Fact]
    public void The_size_warning_starts_above_50_MB()
    {
        Assert.False(ChangeHistoryRetention.IsLarge(50L * 1024 * 1024));
        Assert.True(ChangeHistoryRetention.IsLarge((50L * 1024 * 1024) + 1));
    }

    /// <summary>Moves the clock, then waits for the prune the tick started to delete
    /// something — each tick these tests make has something to delete.</summary>
    private async Task TickAsync(ChangeHistoryWorker worker, TimeSpan by)
    {
        var pruned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged() => pruned.TrySetResult();
        worker.Changed += OnChanged;
        try
        {
            _time.Advance(by);
            await pruned.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        }
        finally
        {
            worker.Changed -= OnChanged;
        }
    }

    private async Task<Guid> SeedAsync(string title, DateTimeOffset at)
    {
        var repository = new SqliteTaskRepository(_settings.RootDirectory);
        var changeSet = ChangeSet.Start(ChangeActor.Person, title);
        using (ChangeContext.Enter(changeSet))
        {
            await repository.SaveAsync(new TaskItem(title, string.Empty, EntryType.Task), Token);
        }

        await using var connection = new SqliteConnection($"Data Source={_settings.DatabasePath}");
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE change_sets SET started_at = $at WHERE id = $id;
            UPDATE revisions SET recorded_at = $at WHERE change_set_id = $id;
            """;
        command.Parameters.AddWithValue("$id", changeSet.Id.ToString());
        command.Parameters.AddWithValue("$at", at.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(Token);
        return changeSet.Id;
    }

    private async Task<List<Guid>> ChangeSetIdsAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_settings.DatabasePath}");
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM change_sets ORDER BY started_at;";
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token)) ids.Add(Guid.Parse(reader.GetString(0)));
        return ids;
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={_settings.DatabasePath}");
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync(Token))!;
    }
}
