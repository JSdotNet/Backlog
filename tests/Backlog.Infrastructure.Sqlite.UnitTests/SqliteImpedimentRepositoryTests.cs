using Backlog.Infrastructure.Sqlite.Progress;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The stored impediments: one row per impediment in <c>progress_impediments</c>, the
/// whole impediment as one document, which is also the <c>impediment</c> document that
/// travels. What goes in comes back out unchanged, a copy from another device is kept
/// verbatim at its stamp, and a row that will not parse is skipped rather than failing
/// the read.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteImpedimentRepositoryTests : IDisposable
{
    private static readonly DateOnly Wed14 = new(2026, 10, 14);

    private readonly string _dir;
    private readonly string _other;
    private readonly SqliteImpedimentRepository _repository;

    public SqliteImpedimentRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "impediment-tests-" + Guid.NewGuid().ToString("N"));
        _other = Path.Combine(Path.GetTempPath(), "impediment-tests-" + Guid.NewGuid().ToString("N"));
        _repository = new SqliteImpedimentRepository(_dir);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static Impediment Escalated(string title = "Bank sample files missing", DateTimeOffset? createdAt = null)
    {
        var impediment = Impediment.Raise(Guid.NewGuid(), title, Wed14, createdAt).Value;
        Assert.True(impediment.MoveTo(ImpedimentState.Reported, Wed14.AddDays(1), "bank support").IsSuccess);
        Assert.True(impediment.MoveTo(ImpedimentState.Escalated, Wed14.AddDays(2), "account manager").IsSuccess);
        impediment.SetCheckAgain(new DateOnly(2026, 10, 20));
        impediment.EditNote("Asked for the sample twice.\nNo answer yet.");
        impediment.HoldBack(Guid.NewGuid());
        impediment.HoldBack(Guid.NewGuid());
        return impediment;
    }

    private static void AssertSame(Impediment expected, Impediment? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.ReportedTo, actual.ReportedTo);
        Assert.Equal(expected.HandledBy, actual.HandledBy);
        Assert.Equal(expected.CheckAgain, actual.CheckAgain);
        Assert.Equal(expected.Note, actual.Note);
        Assert.Equal(expected.History, actual.History);
        Assert.Equal(expected.HoldsBack, actual.HoldsBack);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
    }

    /// <summary>The phone reads the document this store writes with
    /// <c>ProgressDocuments.ReadImpediment</c> (local ADR 0035 §8), and what it reads is the
    /// impediment the desktop holds, history and all.</summary>
    [Fact]
    public async Task The_stored_document_reads_on_the_phone_as_the_impediment_the_desktop_holds()
    {
        var impediment = Escalated(createdAt: new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero));
        await _repository.SaveAsync(impediment, Cancellation);

        var stored = Assert.Single(await _repository.ReadAllAsync(Cancellation));
        var read = Modules.Progress.Abstractions.ProgressDocuments.ReadImpediment(stored.Content, impediment.Id);

        Assert.NotNull(read);
        Assert.Equivalent(Modules.Progress.Services.ImpedimentMapper.ToDto(impediment), read.Impediment, strict: true);
        Assert.Equal(impediment.CreatedAt, read.CreatedAt);
    }

    [Fact]
    public async Task A_first_run_has_no_impediment()
    {
        Assert.Empty(await _repository.ListAsync(Cancellation));
        Assert.Null(await _repository.GetAsync(Guid.NewGuid(), Cancellation));
        Assert.Empty(await _repository.ReadAllAsync(Cancellation));
    }

    [Fact]
    public async Task An_impediment_with_its_history_and_tasks_round_trips_unchanged()
    {
        var impediment = Escalated(createdAt: new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero));

        await _repository.SaveAsync(impediment, Cancellation);

        AssertSame(impediment, await _repository.GetAsync(impediment.Id, Cancellation));
        AssertSame(impediment, Assert.Single(await _repository.ListAsync(Cancellation)));
    }

    [Fact]
    public async Task A_resolved_impediment_reads_back_resolved_on_its_day()
    {
        var impediment = Impediment.Raise(Guid.NewGuid(), "Credit note numbering unclear", Wed14).Value;
        Assert.True(impediment.MoveTo(ImpedimentState.Resolved, Wed14.AddDays(1)).IsSuccess);

        await _repository.SaveAsync(impediment, Cancellation);

        var read = await _repository.GetAsync(impediment.Id, Cancellation);
        Assert.Equal(ImpedimentState.Resolved, read!.State);
        Assert.Equal(Wed14.AddDays(1), read.ResolvedOn);
    }

    [Fact]
    public async Task Saving_again_replaces_the_row_and_moves_its_stamp_forward()
    {
        var impediment = Escalated();
        await _repository.SaveAsync(impediment, Cancellation);
        var first = (await _repository.ReadAsync(impediment.Id, Cancellation))!.UpdatedAt;

        Assert.True(impediment.MoveTo(ImpedimentState.BeingHandled, Wed14.AddDays(3), "bank").IsSuccess);
        await _repository.SaveAsync(impediment, Cancellation);

        Assert.Equal(1, await CountRowsAsync());
        Assert.Equal(ImpedimentState.BeingHandled, (await _repository.GetAsync(impediment.Id, Cancellation))!.State);
        Assert.True((await _repository.ReadAsync(impediment.Id, Cancellation))!.UpdatedAt > first);
    }

    [Fact]
    public async Task The_stored_document_names_the_state_by_name_and_days_as_dates()
    {
        await _repository.SaveAsync(Escalated(), Cancellation);

        var document = await ScalarAsync("SELECT document FROM progress_impediments;");

        Assert.Contains("\"state\":\"Escalated\"", document, StringComparison.Ordinal);
        Assert.Contains("\"on\":\"2026-10-16\"", document, StringComparison.Ordinal);
        Assert.Contains("\"checkAgain\":\"2026-10-20\"", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Impediments_list_in_the_order_they_were_raised()
    {
        var later = Escalated("Later", new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero));
        var earlier = Escalated("Earlier", new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero));

        await _repository.SaveAsync(later, Cancellation);
        await _repository.SaveAsync(earlier, Cancellation);

        Assert.Equal(["Earlier", "Later"], (await _repository.ListAsync(Cancellation)).Select(impediment => impediment.Title));
    }

    [Fact]
    public async Task A_row_that_will_not_parse_is_skipped()
    {
        await _repository.SaveAsync(Escalated(), Cancellation);
        await ExecuteAsync("INSERT INTO progress_impediments (id, document, created_at, updated_at) VALUES ('broken', '{not json', '2026-10-01', '2026-10-01');");

        Assert.Single(await _repository.ListAsync(Cancellation));
    }

    [Fact]
    public async Task A_copy_from_another_device_is_kept_verbatim_at_its_stamp()
    {
        var there = new SqliteImpedimentRepository(_other);
        var impediment = Escalated();
        await there.SaveAsync(impediment, Cancellation);
        var copy = (await there.ReadAsync(impediment.Id, Cancellation))!;

        Assert.True(await _repository.TryWriteAsync(copy, Cancellation));

        Assert.Equal(copy, await _repository.ReadAsync(impediment.Id, Cancellation));
        AssertSame(impediment, await _repository.GetAsync(impediment.Id, Cancellation));
        Assert.Equal(impediment.Title, copy.Title);
    }

    [Fact]
    public async Task A_copy_that_does_not_read_as_that_impediment_is_refused()
    {
        var there = new SqliteImpedimentRepository(_other);
        var impediment = Escalated();
        await there.SaveAsync(impediment, Cancellation);
        var copy = (await there.ReadAsync(impediment.Id, Cancellation))!;

        Assert.False(await _repository.TryWriteAsync(copy with { Id = Guid.NewGuid() }, Cancellation));
        Assert.False(await _repository.TryWriteAsync(copy with { Content = "{not json" }, Cancellation));
        Assert.False(await _repository.TryWriteAsync(copy with { Content = "{\"id\":\"" + copy.Id + "\",\"title\":\"x\",\"history\":[]}" }, Cancellation));
        Assert.Empty(await _repository.ReadAllAsync(Cancellation));
    }

    [Fact]
    public async Task A_save_after_a_copy_stamped_ahead_still_moves_the_stamp_forward()
    {
        var impediment = Escalated();
        var ahead = new ImpedimentReplicaCopyDto(
            impediment.Id,
            impediment.Title,
            System.Text.Json.JsonSerializer.Serialize(ImpedimentDocument.From(impediment), ImpedimentDocument.JsonOptions),
            DateTimeOffset.UtcNow.AddHours(1));
        Assert.True(await _repository.TryWriteAsync(ahead, Cancellation));

        await _repository.SaveAsync(impediment, Cancellation);

        Assert.True((await _repository.ReadAsync(impediment.Id, Cancellation))!.UpdatedAt > ahead.UpdatedAt);
    }

    /// <summary>The migration: the table is created on first open under a stamp of its own,
    /// beside the goals', and a file the goals already set up gains it.</summary>
    [Fact]
    public async Task The_table_is_set_up_beside_the_goals_under_its_own_stamp()
    {
        await new SqliteGoalRepository(_dir).ListAsync(Cancellation);
        await _repository.ListAsync(Cancellation);

        Assert.Equal("1", await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'progress_impediments';"));
        Assert.Equal("1", await ScalarAsync("SELECT COUNT(*) FROM schema_stamps WHERE owner = 'progress_impediments';"));
        Assert.Equal("1", await ScalarAsync("SELECT COUNT(*) FROM schema_stamps WHERE owner = 'progress_goals';"));
    }

    [Fact]
    public async Task The_rooted_repository_follows_its_folder()
    {
        var root = _dir;
        var rooted = new RootedSqliteImpedimentRepository(() => root);
        var impediment = Escalated();
        await rooted.SaveAsync(impediment, Cancellation);

        root = _other;

        Assert.Empty(await rooted.ListAsync(Cancellation));
        Assert.NotNull(await new SqliteImpedimentRepository(_dir).GetAsync(impediment.Id, Cancellation));
    }

    private async Task<long> CountRowsAsync() =>
        long.Parse(await ScalarAsync("SELECT COUNT(*) FROM progress_impediments;"), System.Globalization.CultureInfo.InvariantCulture);

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
