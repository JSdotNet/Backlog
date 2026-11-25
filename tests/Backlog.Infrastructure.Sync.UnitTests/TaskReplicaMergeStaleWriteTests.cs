using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks;
using Backlog.Tests;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The store's stale-write check (local saves are refused when the row changed after
/// they read it) does not reach the replica merge: a pulled copy is decided by the
/// merge's own precedence rule alone, against the real store. What the check does do
/// is protect the pulled copy from a local writer that read the row before it landed.
/// </summary>
public sealed class TaskReplicaMergeStaleWriteTests : IDisposable
{
    private static readonly Guid Phone = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Noon = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _temp = new("backlog-merge-stale");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static TaskChangeRecord Pulled(Guid id, string title, DateTimeOffset stamp, long serverTimestamp)
    {
        var copy = TaskChanges.Task(title, Noon, id);
        copy.LoadStamps(stamp, null);
        return new TaskChangeRecord(TaskReplicaMerge.ToChange(copy), Phone, serverTimestamp);
    }

    [Fact]
    public async Task A_later_pulled_copy_is_written_over_a_row_edited_here_since_any_read()
    {
        var store = new SqliteTaskRepository(_temp.Path);
        var local = TaskChanges.Task("Local", Noon);
        await store.SaveAsync(local, Token);

        var read = (await store.GetAsync(local.Id, Token))!;
        read.SetArea("edited here");
        await store.SaveAsync(read, Token);

        var outcome = await new TaskReplicaMerge(store).ApplyAsync(
            [Pulled(local.Id, "From the phone", read.UpdatedAt.AddMinutes(1), 200)], Token);

        Assert.Equal(1, outcome.Applied);
        Assert.Equal("From the phone", (await store.GetAsync(local.Id, Token))!.Title);
    }

    [Fact]
    public async Task An_older_pulled_copy_is_still_held_by_precedence_alone()
    {
        var store = new SqliteTaskRepository(_temp.Path);
        var local = TaskChanges.Task("Local", Noon);
        local.LoadStamps(Noon.AddHours(1), null);
        await store.SaveAsync(local, Token);

        var outcome = await new TaskReplicaMerge(store).ApplyAsync(
            [Pulled(local.Id, "Older on the phone", Noon, 200)], Token);

        Assert.Equal(0, outcome.Applied);
        Assert.Equal("Local", (await store.GetAsync(local.Id, Token))!.Title);
    }

    [Fact]
    public async Task A_local_writer_that_read_before_the_merge_is_refused_after_it()
    {
        var store = new SqliteTaskRepository(_temp.Path);
        var local = TaskChanges.Task("Local", Noon);
        await store.SaveAsync(local, Token);

        var readBeforeMerge = (await store.GetAsync(local.Id, Token))!;

        await new TaskReplicaMerge(store).ApplyAsync(
            [Pulled(local.Id, "From the phone", Noon.AddMinutes(5), 200)], Token);

        readBeforeMerge.SetArea("would erase the phone's edit");
        await Assert.ThrowsAsync<StaleTaskWriteException>(() => store.SaveAsync(readBeforeMerge, Token));

        Assert.Equal("From the phone", (await store.GetAsync(local.Id, Token))!.Title);
    }

    public void Dispose()
    {
        // Pooled connections hold the file open; let this test's own go before the
        // folder does. Only its own: clearing every pool races the parallel classes.
        using (var own = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={SqliteTaskRepository.DatabasePathFor(_temp.Path)}"))
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(own);
        }

        _temp.Dispose();
    }
}
