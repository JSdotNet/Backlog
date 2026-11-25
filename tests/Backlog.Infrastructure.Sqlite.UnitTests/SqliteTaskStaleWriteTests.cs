using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// Task saves are optimistic: a task read from the store is saved only while the
/// store still holds the version it was read at. Two sessions editing one entry at
/// once used to lose one edit with nothing said; these pin that the first save
/// lands and the second is refused, writing nothing.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteTaskStaleWriteTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new("backlog-sqlite-stale");
    private readonly SqliteTaskRepository _repository;

    public SqliteTaskStaleWriteTests() => _repository = new SqliteTaskRepository(_temp.Path);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<Guid> StoredAsync(string title = "Shared entry")
    {
        var task = new TaskItem(title, "Body.", EntryType.Task);
        await _repository.SaveAsync(task, Token);
        return task.Id;
    }

    [Fact]
    public async Task The_first_of_two_saves_from_one_read_lands_and_the_second_is_refused()
    {
        var id = await StoredAsync();

        // Two sessions read the same version.
        var first = (await _repository.GetAsync(id, Token))!;
        var second = (await _repository.GetAsync(id, Token))!;

        first.SetArea("first");
        second.SetArea("second");

        await _repository.SaveAsync(first, Token);
        var refused = await Assert.ThrowsAsync<StaleTaskWriteException>(() => _repository.SaveAsync(second, Token));

        Assert.Equal(id, refused.TaskId);
        Assert.Equal("first", (await _repository.GetAsync(id, Token))!.Area);
    }

    [Fact]
    public async Task Of_two_concurrent_saves_from_one_read_exactly_one_lands()
    {
        var id = await StoredAsync();

        // Two stores over the one file: two sessions, two connections.
        var other = new SqliteTaskRepository(_temp.Path);
        var mine = (await _repository.GetAsync(id, Token))!;
        var theirs = (await other.GetAsync(id, Token))!;

        mine.SetArea("mine");
        theirs.SetArea("theirs");

        var saves = new[]
        {
            Task.Run(() => _repository.SaveAsync(mine, Token), Token),
            Task.Run(() => other.SaveAsync(theirs, Token), Token),
        };

        var outcomes = await Task.WhenAll(saves.Select(async save =>
        {
            try
            {
                await save;
                return (Landed: true, Refused: false);
            }
            catch (StaleTaskWriteException)
            {
                return (Landed: false, Refused: true);
            }
        }));

        Assert.Equal(1, outcomes.Count(o => o.Landed));
        Assert.Equal(1, outcomes.Count(o => o.Refused));

        var winner = outcomes[0].Landed ? "mine" : "theirs";
        Assert.Equal(winner, (await _repository.GetAsync(id, Token))!.Area);
    }

    [Fact]
    public async Task One_instance_saved_twice_in_a_row_is_not_refused()
    {
        var id = await StoredAsync();
        var task = (await _repository.GetAsync(id, Token))!;

        task.SetArea("once");
        await _repository.SaveAsync(task, Token);
        task.SetArea("twice");
        await _repository.SaveAsync(task, Token);

        Assert.Equal("twice", (await _repository.GetAsync(id, Token))!.Area);
    }

    [Fact]
    public async Task A_read_after_a_refusal_can_save()
    {
        var id = await StoredAsync();
        var first = (await _repository.GetAsync(id, Token))!;
        var second = (await _repository.GetAsync(id, Token))!;

        first.SetArea("first");
        await _repository.SaveAsync(first, Token);
        second.SetArea("second");
        await Assert.ThrowsAsync<StaleTaskWriteException>(() => _repository.SaveAsync(second, Token));

        var reread = (await _repository.GetAsync(id, Token))!;
        reread.SetArea("second");
        await _repository.SaveAsync(reread, Token);

        Assert.Equal("second", (await _repository.GetAsync(id, Token))!.Area);
    }

    [Fact]
    public async Task A_batch_with_one_stale_task_writes_none_of_it()
    {
        var a = await StoredAsync("A");
        var b = await StoredAsync("B");

        var staleB = (await _repository.GetAsync(b, Token))!;
        var freshB = (await _repository.GetAsync(b, Token))!;
        freshB.SetArea("elsewhere");
        await _repository.SaveAsync(freshB, Token);

        var loadedA = (await _repository.GetAsync(a, Token))!;
        loadedA.SetOrder(7);
        staleB.SetOrder(8);

        await Assert.ThrowsAsync<StaleTaskWriteException>(
            () => _repository.SaveManyAsync([loadedA, staleB], Token));

        Assert.Equal(0, (await _repository.GetAsync(a, Token))!.Order);
        Assert.Equal("elsewhere", (await _repository.GetAsync(b, Token))!.Area);

        // The rolled-back task still carries the version the store holds, so it
        // saves on its own.
        await _repository.SaveAsync(loadedA, Token);
        Assert.Equal(7, (await _repository.GetAsync(a, Token))!.Order);
    }

    /// <summary>A copy no store handed out — the replica merge's, built from another
    /// device's document — has no version to be stale against and is written whatever
    /// the row holds. Whether it may replace the row is the merge's own rule.</summary>
    [Fact]
    public async Task A_task_no_store_handed_out_is_written_unconditionally()
    {
        var id = await StoredAsync();
        var held = (await _repository.GetAsync(id, Token))!;
        held.SetArea("local edit");
        await _repository.SaveAsync(held, Token);

        var copy = new TaskItem(
            id, "Shared entry", "From the other device.", EntryType.Task, EntryStatus.Draft, Priority.Medium,
            repoIds: [], tags: [], sourceInboxId: null, createdAt: held.CreatedAt);
        copy.LoadStamps(held.UpdatedAt.AddMinutes(1), deletedAt: null);
        Assert.Null(copy.LoadedUpdatedAt);

        await _repository.SaveAsync(copy, Token);

        Assert.Equal("From the other device.", (await _repository.GetAsync(id, Token))!.ContentMd);
    }

    /// <summary>A row from before updated_at was stamped reads its stamp as
    /// created_at, and compares as the same value on save.</summary>
    [Fact]
    public async Task A_row_without_a_stamp_saves_against_its_creation_time()
    {
        var id = await StoredAsync();

        await using (var connection = new SqliteConnection($"Data Source={_repository.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync(Token);
            await using var clear = connection.CreateCommand();
            clear.CommandText = "UPDATE tasks SET updated_at = NULL;";
            await clear.ExecuteNonQueryAsync(Token);
        }

        var task = (await _repository.GetAsync(id, Token))!;
        task.SetArea("saved");
        await _repository.SaveAsync(task, Token);

        Assert.Equal("saved", (await _repository.GetAsync(id, Token))!.Area);
    }

    /// <summary>A stamp that does not read back as the text it was stored as — written
    /// by hand, or in another form — can never compare equal, so the row is saved
    /// unchecked rather than refused for good.</summary>
    [Fact]
    public async Task A_row_whose_stamp_does_not_round_trip_still_saves()
    {
        var id = await StoredAsync();

        await using (var connection = new SqliteConnection($"Data Source={_repository.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync(Token);
            await using var rewrite = connection.CreateCommand();
            rewrite.CommandText = "UPDATE tasks SET updated_at = '2026-11-24T12:00:00Z';";
            await rewrite.ExecuteNonQueryAsync(Token);
        }

        var task = (await _repository.GetAsync(id, Token))!;
        Assert.Null(task.LoadedUpdatedAt);

        task.SetArea("saved");
        await _repository.SaveAsync(task, Token);

        Assert.Equal("saved", (await _repository.GetAsync(id, Token))!.Area);
    }

    public void Dispose() => _temp.Dispose();
}
