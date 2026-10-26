using Backlog.Infrastructure.Sqlite.Progress;
using Backlog.Modules.Progress;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The stored goals: one row per goal in <c>progress_goals</c>, the whole goal as one
/// document, which is also the <c>goal</c> document that travels. What goes in comes back
/// out unchanged, a deleted goal leaves a tombstone that travels, and a row that will not
/// parse is skipped rather than failing the read.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteGoalRepositoryTests : IDisposable
{
    private static readonly DateOnly Due = new(2026, 10, 31);
    private static readonly DateOnly Today = new(2026, 10, 17);

    private readonly string _dir;
    private readonly string _other;
    private readonly SqliteGoalRepository _repository;

    public SqliteGoalRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "goal-tests-" + Guid.NewGuid().ToString("N"));
        _other = Path.Combine(Path.GetTempPath(), "goal-tests-" + Guid.NewGuid().ToString("N"));
        _repository = new SqliteGoalRepository(_dir);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static Goal Goal(string title = "Customers get paid faster", DateTimeOffset? createdAt = null)
    {
        return Modules.Progress.DomainModels.Goal.Restore(
            Guid.NewGuid(),
            title,
            "Invoices settle in a day",
            Due,
            [
                GoalAttachment.ForPlan("+bank-integration", Today).Value,
                GoalAttachment.ForTask(Guid.NewGuid(), Today).Value,
                GoalAttachment.ForPullRequest("JSdotNet/Backlog", 1352, Today.AddDays(1)).Value,
            ],
            createdAt);
    }

    [Fact]
    public async Task A_first_run_has_no_goal()
    {
        Assert.Empty(await _repository.ListAsync(Cancellation));
        Assert.Null(await _repository.GetAsync(Guid.NewGuid(), Cancellation));
        Assert.Empty(await _repository.ReadAllAsync(Cancellation));
    }

    [Fact]
    public async Task A_goal_with_its_attachments_round_trips_unchanged()
    {
        var goal = Goal(createdAt: new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero));

        await _repository.SaveAsync(goal, Cancellation);
        var loaded = await _repository.GetAsync(goal.Id, Cancellation);

        Assert.NotNull(loaded);
        Assert.Equal(goal.Title, loaded.Title);
        Assert.Equal(goal.Outcome, loaded.Outcome);
        Assert.Equal(goal.DueDate, loaded.DueDate);
        Assert.Equal(goal.CreatedAt, loaded.CreatedAt);
        Assert.Equal(goal.Attachments, loaded.Attachments);
        Assert.Equal(goal.Attachments.Select(a => a.AttachedOn), loaded.Attachments.Select(a => a.AttachedOn));
    }

    [Fact]
    public async Task Goals_list_in_the_order_they_were_set_and_a_save_replaces_the_row()
    {
        var first = Goal("First");
        var second = Goal("Second");

        await _repository.SaveAsync(first, Cancellation);
        await _repository.SaveAsync(second, Cancellation);
        first.Rename("First, renamed");
        await _repository.SaveAsync(first, Cancellation);

        var all = await _repository.ListAsync(Cancellation);

        Assert.Equal([first.Id, second.Id], all.Select(goal => goal.Id));
        Assert.Equal("First, renamed", all[0].Title);
        Assert.Equal(2, await CountRowsAsync());
    }

    [Fact]
    public async Task The_document_writes_dates_as_days_and_kinds_by_name()
    {
        await _repository.SaveAsync(Goal(), Cancellation);

        var document = await ScalarAsync("SELECT document FROM progress_goals;");

        Assert.Contains("\"dueDate\":\"2026-10-31\"", document, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"Plan\"", document, StringComparison.Ordinal);
        Assert.Contains("\"plan\":\"bank-integration\"", document, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"PullRequest\"", document, StringComparison.Ordinal);
        Assert.Contains("\"version\":1", document, StringComparison.Ordinal);
    }

    /// <summary>A goal without a due date is written with no <c>dueDate</c>, so a build that
    /// reads one finds none rather than a null it cannot hold, and it reads back without one.</summary>
    [Fact]
    public async Task A_goal_without_a_due_date_is_written_without_one_and_reads_back_without_one()
    {
        var goal = Goal();
        goal.ChangeDueDate(null);

        await _repository.SaveAsync(goal, Cancellation);

        Assert.DoesNotContain("dueDate", await ScalarAsync("SELECT document FROM progress_goals;"), StringComparison.Ordinal);
        Assert.Null((await _repository.GetAsync(goal.Id, Cancellation))!.DueDate);
    }

    /// <summary>An older build that required a date reads a missing one as <c>0001-01-01</c>
    /// and writes that back on its next edit; it reads here as no date.</summary>
    [Fact]
    public async Task A_copy_holding_the_first_day_of_year_one_reads_as_a_goal_without_a_due_date()
    {
        var id = Guid.NewGuid();
        var copy = new GoalReplicaCopyDto(id, "x", $$"""{"version":1,"id":"{{id}}","title":"T","dueDate":"0001-01-01"}""", DateTimeOffset.UtcNow);

        Assert.True(await _repository.TryWriteAsync(copy, Cancellation));

        Assert.Null((await _repository.GetAsync(id, Cancellation))!.DueDate);
    }

    [Fact]
    public async Task A_row_that_will_not_parse_is_skipped_and_left_as_it_is()
    {
        var good = Goal();
        await _repository.SaveAsync(good, Cancellation);
        await ExecuteAsync("INSERT INTO progress_goals (id, document, created_at, updated_at) VALUES ('broken', '{not json', '2026-10-01', '2026-10-01');");

        var all = await _repository.ListAsync(Cancellation);

        Assert.Equal([good.Id], all.Select(goal => goal.Id));
        Assert.Equal(2, await CountRowsAsync());
    }

    /// <summary>Deleting leaves a tombstone, stamped later than the goal was, so the
    /// deletion is what travels and an older copy is refused on its stamp.</summary>
    [Fact]
    public async Task A_deleted_goal_is_gone_but_its_tombstone_travels()
    {
        var goal = Goal();
        await _repository.SaveAsync(goal, Cancellation);
        var saved = Assert.Single(await _repository.ReadAllAsync(Cancellation));

        await _repository.DeleteAsync(goal.Id, Cancellation);

        Assert.Empty(await _repository.ListAsync(Cancellation));
        Assert.Null(await _repository.GetAsync(goal.Id, Cancellation));

        var tombstone = Assert.Single(await _repository.ReadAllAsync(Cancellation));
        Assert.True(tombstone.UpdatedAt > saved.UpdatedAt);
        Assert.Contains("\"deletedAt\"", tombstone.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("bank-integration", tombstone.Content, StringComparison.Ordinal);
        Assert.Equal(goal.Title, tombstone.Title);
    }

    [Fact]
    public async Task Deleting_an_id_it_does_not_hold_writes_nothing()
    {
        await _repository.DeleteAsync(Guid.NewGuid(), Cancellation);

        Assert.Equal(0, await CountRowsAsync());
    }

    /// <summary>A copy from another device is kept verbatim at the stamp it arrived with,
    /// and reads back as the goal it is.</summary>
    [Fact]
    public async Task A_copy_from_another_device_is_written_verbatim_at_its_stamp()
    {
        var there = new SqliteGoalRepository(_other);
        var goal = Goal();
        await there.SaveAsync(goal, Cancellation);
        var copy = Assert.Single(await there.ReadAllAsync(Cancellation));

        Assert.True(await _repository.TryWriteAsync(copy, Cancellation));

        var here = await _repository.ReadAsync(goal.Id, Cancellation);
        Assert.Equal(copy, here);
        Assert.Equal(goal.Attachments, (await _repository.GetAsync(goal.Id, Cancellation))!.Attachments);
    }

    /// <summary>A sets G1, then G2, then edits G1. B, pulling both for the first time,
    /// lists them in the order they were set, as A does — not by the stamp of the last edit.</summary>
    [Fact]
    public async Task Goals_pulled_for_the_first_time_list_in_the_order_they_were_set()
    {
        var there = new SqliteGoalRepository(_other);
        var first = Goal("First", createdAt: new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero));
        var second = Goal("Second", createdAt: new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));
        await there.SaveAsync(first, Cancellation);
        await there.SaveAsync(second, Cancellation);
        first.Rename("First, edited later");
        await there.SaveAsync(first, Cancellation);

        var copies = await there.ReadAllAsync(Cancellation);
        foreach (var copy in copies.OrderByDescending(copy => copy.UpdatedAt))
        {
            Assert.True(await _repository.TryWriteAsync(copy, Cancellation));
        }

        Assert.Equal([first.Id, second.Id], (await there.ListAsync(Cancellation)).Select(goal => goal.Id));
        Assert.Equal([first.Id, second.Id], (await _repository.ListAsync(Cancellation)).Select(goal => goal.Id));
    }

    [Fact]
    public async Task A_copy_that_is_not_a_goal_or_names_another_is_refused_and_nothing_is_written()
    {
        var goal = Goal();
        await _repository.SaveAsync(goal, Cancellation);
        var held = await _repository.ReadAsync(goal.Id, Cancellation);

        var garbage = new GoalReplicaCopyDto(goal.Id, "x", "{not json", DateTimeOffset.UtcNow.AddDays(1));
        var untitled = new GoalReplicaCopyDto(goal.Id, "x", $$"""{"version":1,"id":"{{goal.Id}}","title":"","dueDate":"2026-10-31"}""", DateTimeOffset.UtcNow.AddDays(1));
        var unknownKind = new GoalReplicaCopyDto(goal.Id, "x", $$"""{"version":1,"id":"{{goal.Id}}","title":"T","dueDate":"2026-10-31","attachments":[{"kind":"Epic"}]}""", DateTimeOffset.UtcNow.AddDays(1));
        var another = new GoalReplicaCopyDto(goal.Id, "x", $$"""{"version":1,"id":"{{Guid.NewGuid()}}","title":"T","dueDate":"2026-10-31"}""", DateTimeOffset.UtcNow.AddDays(1));

        Assert.False(await _repository.TryWriteAsync(garbage, Cancellation));
        Assert.False(await _repository.TryWriteAsync(untitled, Cancellation));
        Assert.False(await _repository.TryWriteAsync(unknownKind, Cancellation));
        Assert.False(await _repository.TryWriteAsync(another, Cancellation));

        Assert.Equal(held, await _repository.ReadAsync(goal.Id, Cancellation));
    }

    /// <summary>A save after a copy whose stamp runs ahead of this clock is still later
    /// than it, so the edit made here is never read as older.</summary>
    [Fact]
    public async Task A_save_after_a_copy_from_the_future_stamps_later_than_it()
    {
        var goal = Goal();
        await _repository.SaveAsync(goal, Cancellation);
        var ahead = (await _repository.ReadAsync(goal.Id, Cancellation))! with { UpdatedAt = DateTimeOffset.UtcNow.AddHours(3) };
        Assert.True(await _repository.TryWriteAsync(ahead, Cancellation));

        goal.Rename("Edited here");
        await _repository.SaveAsync(goal, Cancellation);

        Assert.True((await _repository.ReadAsync(goal.Id, Cancellation))!.UpdatedAt > ahead.UpdatedAt);
    }

    /// <summary>The goals' setup runs under a stamp of its own, and a file an older build
    /// gave the retired <c>progress_series</c> table gains the goals' table and keeps that
    /// one as it was: local ADR 0006 adds and never drops (ADR 0035, Consequences).</summary>
    [Fact]
    public async Task The_table_is_set_up_under_its_own_stamp_and_the_retired_table_is_kept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_repository.DatabasePath)!);
        await ExecuteAsync("""
            CREATE TABLE progress_series (id TEXT PRIMARY KEY NOT NULL, document TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            INSERT INTO progress_series VALUES ('5a0c7a51-6f4e-4f2b-8d7e-1c2b3a4d5e60', '{}', '2026-10-12', '2026-10-12');
            """);

        await _repository.ListAsync(Cancellation);

        Assert.Equal("1", await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'progress_goals';"));
        Assert.Equal("1", await ScalarAsync("SELECT COUNT(*) FROM schema_stamps WHERE owner = 'progress_goals';"));
        Assert.Equal("1", await ScalarAsync("SELECT COUNT(*) FROM progress_series;"));
    }

    [Fact]
    public async Task The_rooted_repository_follows_its_folder()
    {
        var root = _dir;
        var rooted = new RootedSqliteGoalRepository(() => root);
        var goal = Goal();
        await rooted.SaveAsync(goal, Cancellation);

        root = _other;

        Assert.Empty(await rooted.ListAsync(Cancellation));
        Assert.NotNull(await new SqliteGoalRepository(_dir).GetAsync(goal.Id, Cancellation));
    }

    private async Task<long> CountRowsAsync() =>
        long.Parse(await ScalarAsync("SELECT COUNT(*) FROM progress_goals;"), System.Globalization.CultureInfo.InvariantCulture);

    private async Task<string> ScalarAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={_repository.DatabasePath}");
        await connection.OpenAsync(Cancellation);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(Cancellation), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={_repository.DatabasePath}");
        await connection.OpenAsync(Cancellation);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Cancellation);
    }

    /// <summary>The phone reads the document this store writes with
    /// <c>ProgressDocuments.ReadGoal</c>, in the published surface (local ADR 0035 §8): what
    /// it reads is the goal the desktop holds, and a deleted goal reads as deleted.</summary>
    [Fact]
    public async Task The_stored_document_reads_on_the_phone_as_the_goal_the_desktop_holds()
    {
        var goal = Goal(createdAt: new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));
        await _repository.SaveAsync(goal, Cancellation);

        var stored = Assert.Single(await _repository.ReadAllAsync(Cancellation));
        var read = Modules.Progress.Abstractions.ProgressDocuments.ReadGoal(stored.Content, goal.Id);

        Assert.NotNull(read);
        Assert.False(read.Deleted);
        Assert.Equal(goal.CreatedAt, read.CreatedAt);
        Assert.Equal(Modules.Progress.Services.GoalMapper.ToDto(goal).Title, read.Goal.Title);
        Assert.Equal(
            Modules.Progress.Services.GoalMapper.ToDto(goal).Attachments,
            read.Goal.Attachments);

        await _repository.DeleteAsync(goal.Id, Cancellation);

        var tombstone = Assert.Single(await _repository.ReadAllAsync(Cancellation));
        Assert.True(Modules.Progress.Abstractions.ProgressDocuments.ReadGoal(tombstone.Content, goal.Id)!.Deleted);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (var dir in new[] { _dir, _other })
        {
            if (!Directory.Exists(dir)) continue;
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
