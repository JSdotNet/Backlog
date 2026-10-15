using System.Text.Json.Nodes;

using Backlog.Infrastructure.Sqlite.Progress;
using Backlog.Modules.Progress;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The series row as the document that travels between devices: read exactly as stored
/// with its stamp, and written verbatim at the stamp a copy arrived with — never a copy
/// that does not read as a series, nor one that names another series.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteSprintSeriesReplicaTests : IDisposable
{
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly DateTimeOffset Morning = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "progress-replica-" + Guid.NewGuid().ToString("N"));
    private readonly string _other = Path.Combine(Path.GetTempPath(), "progress-replica-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (var dir in new[] { _dir, _other })
        {
            if (!Directory.Exists(dir)) continue;
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static SprintSeries Series(string name)
    {
        var series = SprintSeries.Create(Guid.NewGuid(), ["backlog"], name).Value;
        series.StartSprint(Monday, "Ship sync");
        return series;
    }

    [Fact]
    public async Task A_device_that_never_saved_a_series_holds_nothing_to_send()
    {
        ISprintSeriesReplicaStore store = new SqliteSprintSeriesRepository(_dir);

        Assert.Empty(await store.ReadAllAsync(Cancellation));
        Assert.Null(await store.ReadAsync(Guid.NewGuid(), Cancellation));
    }

    [Fact]
    public async Task Each_saved_series_is_read_with_its_name_and_its_own_stamp()
    {
        var repository = new SqliteSprintSeriesRepository(_dir);
        var core = Series("Core");
        var docs = Series("Docs");
        await repository.SaveAsync(core, Cancellation);
        await repository.SaveAsync(docs, Cancellation);

        var all = await repository.ReadAllAsync(Cancellation);

        Assert.Equal([core.Id, docs.Id], all.Select(copy => copy.Id));
        Assert.Equal(["Core", "Docs"], all.Select(copy => copy.Name));
        Assert.Equal(core.Id.ToString(), JsonNode.Parse(all[0].Content)!["id"]!.GetValue<string>());
        Assert.True(all[1].UpdatedAt >= all[0].UpdatedAt);
        Assert.Equal(all[0], await repository.ReadAsync(core.Id, Cancellation));
    }

    /// <summary>The round trip the sync makes: A's row, written on B, reads back on B as
    /// the same text at the same stamp, and loads as the same series — sprints, members,
    /// goal marks, notes and open questions included.</summary>
    [Fact]
    public async Task A_copy_from_another_device_is_stored_verbatim_at_its_own_stamp()
    {
        var a = new SqliteSprintSeriesRepository(_dir);
        var b = new SqliteSprintSeriesRepository(_other);
        var series = Series("Core");
        var task = Guid.NewGuid();
        series.AddTask(1, task, Monday);
        series.MarkGoal(1, task, true);
        series.SetNote(1, task, "Waiting on review");
        series.SetOpenQuestion(1, task, true);
        await a.SaveAsync(series, Cancellation);
        var sent = (await a.ReadAsync(series.Id, Cancellation))!;

        Assert.True(await b.TryWriteAsync(sent, Cancellation));

        Assert.Equal(sent, await b.ReadAsync(series.Id, Cancellation));
        var item = Assert.Single(Assert.Single((await b.GetAsync(series.Id, Cancellation))!.Sprints).Items);
        Assert.Equal(task, item.TaskId);
        Assert.True(item.IsGoal);
        Assert.Equal("Waiting on review", item.Note);
        Assert.True(item.OpenQuestion);
    }

    [Fact]
    public async Task A_later_copy_replaces_the_row_and_an_edit_after_it_is_stamped_later_still()
    {
        var repository = new SqliteSprintSeriesRepository(_dir);
        var series = Series("Core");
        await repository.SaveAsync(series, Cancellation);
        var held = (await repository.ReadAsync(series.Id, Cancellation))!;

        // From a device whose clock runs a day ahead.
        var renamed = (await repository.GetAsync(series.Id, Cancellation))!;
        Assert.True(renamed.Rename("Renamed elsewhere", renamed.NamePattern.Value).IsSuccess);
        var other = new SqliteSprintSeriesRepository(_other);
        await other.SaveAsync(renamed, Cancellation);
        var ahead = (await other.ReadAsync(series.Id, Cancellation))! with { UpdatedAt = held.UpdatedAt.AddDays(1) };

        Assert.True(await repository.TryWriteAsync(ahead, Cancellation));
        Assert.Equal("Renamed elsewhere", (await repository.GetAsync(series.Id, Cancellation))!.Name);

        await repository.SaveAsync((await repository.GetAsync(series.Id, Cancellation))!, Cancellation);
        Assert.True((await repository.ReadAsync(series.Id, Cancellation))!.UpdatedAt > ahead.UpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public async Task A_copy_that_does_not_read_as_a_series_writes_nothing(string content)
    {
        var repository = new SqliteSprintSeriesRepository(_dir);
        var id = Guid.NewGuid();

        Assert.False(await repository.TryWriteAsync(new SprintSeriesReplicaCopyDto(id, "Core", content, Morning), Cancellation));

        Assert.Null(await repository.ReadAsync(id, Cancellation));
    }

    [Fact]
    public async Task A_copy_naming_another_series_writes_nothing()
    {
        var repository = new SqliteSprintSeriesRepository(_dir);
        var series = Series("Core");
        await repository.SaveAsync(series, Cancellation);
        var copy = (await repository.ReadAsync(series.Id, Cancellation))!;
        var elsewhere = Guid.NewGuid();

        Assert.False(await repository.TryWriteAsync(copy with { Id = elsewhere }, Cancellation));

        Assert.Null(await repository.ReadAsync(elsewhere, Cancellation));
    }

    [Fact]
    public async Task The_rooted_store_answers_for_the_current_root()
    {
        var root = _dir;
        var rooted = new RootedSqliteSprintSeriesRepository(() => root);
        var series = Series("Core");
        await rooted.SaveAsync(series, Cancellation);
        var copy = (await rooted.ReadAsync(series.Id, Cancellation))!;

        root = _other;
        Assert.Empty(await rooted.ReadAllAsync(Cancellation));
        Assert.True(await rooted.TryWriteAsync(copy, Cancellation));

        Assert.Equal(copy, Assert.Single(await rooted.ReadAllAsync(Cancellation)));
    }

    // --- A deleted series is a tombstone that travels -----------------------------

    /// <summary>The deleted series is gone from every reading of the aggregate, and its
    /// row stays as a tombstone — its id, its name for the sync log and the moment it
    /// was deleted, with no sprint left in it — stamped later than the series was, so
    /// the next push carries it.</summary>
    [Fact]
    public async Task A_deleted_series_leaves_a_later_tombstone_and_no_series()
    {
        var repository = new SqliteSprintSeriesRepository(_dir);
        var core = Series("Core");
        var docs = Series("Docs");
        await repository.SaveAsync(core, Cancellation);
        await repository.SaveAsync(docs, Cancellation);
        var before = (await repository.ReadAsync(core.Id, Cancellation))!;

        await repository.DeleteAsync(core.Id, Cancellation);

        Assert.Null(await repository.GetAsync(core.Id, Cancellation));
        Assert.Equal([docs.Id], (await repository.ListAsync(Cancellation)).Select(series => series.Id));

        var tombstone = (await repository.ReadAsync(core.Id, Cancellation))!;
        Assert.True(tombstone.UpdatedAt > before.UpdatedAt);
        Assert.Equal("Core", tombstone.Name);
        var document = JsonNode.Parse(tombstone.Content)!;
        Assert.Equal(core.Id.ToString(), document["id"]!.GetValue<string>());
        Assert.NotNull(document["deletedAt"]);
        Assert.Empty(document["sprints"]!.AsArray());
        Assert.Equal([core.Id, docs.Id], (await repository.ReadAllAsync(Cancellation)).Select(copy => copy.Id));
    }

    [Fact]
    public async Task Deleting_a_series_this_device_never_held_writes_nothing()
    {
        var repository = new SqliteSprintSeriesRepository(_dir);
        var id = Guid.NewGuid();

        await repository.DeleteAsync(id, Cancellation);

        Assert.Null(await repository.ReadAsync(id, Cancellation));
    }

    /// <summary>The tombstone is a copy like any other: written on another device
    /// verbatim at its stamp, it takes the series away there too.</summary>
    [Fact]
    public async Task A_tombstone_from_another_device_takes_the_series_away_here()
    {
        var a = new SqliteSprintSeriesRepository(_dir);
        var b = new SqliteSprintSeriesRepository(_other);
        var series = Series("Core");
        await a.SaveAsync(series, Cancellation);
        Assert.True(await b.TryWriteAsync((await a.ReadAsync(series.Id, Cancellation))!, Cancellation));

        await a.DeleteAsync(series.Id, Cancellation);
        var tombstone = (await a.ReadAsync(series.Id, Cancellation))!;

        Assert.True(await b.TryWriteAsync(tombstone, Cancellation));
        Assert.Null(await b.GetAsync(series.Id, Cancellation));
        Assert.Empty(await b.ListAsync(Cancellation));
        Assert.Equal(tombstone, await b.ReadAsync(series.Id, Cancellation));
    }

    [Fact]
    public async Task The_rooted_store_deletes_in_the_current_root()
    {
        var rooted = new RootedSqliteSprintSeriesRepository(() => _dir);
        var series = Series("Core");
        await rooted.SaveAsync(series, Cancellation);

        await rooted.DeleteAsync(series.Id, Cancellation);

        Assert.Empty(await rooted.ListAsync(Cancellation));
    }
}
