using System.Globalization;

using Backlog.Infrastructure.Sqlite.Events;
using Backlog.Modules.Events.DomainModels;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The stored event series: one row per series in <c>events_series</c>, the whole series as one
/// JSON document (local ADR 0050 §2). What goes in comes back out unchanged, saving again keeps
/// one row with a later <c>updated_at</c>, and a row that will not parse is skipped.
/// </summary>
public sealed class SqliteEventSeriesRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private const string BadId = "11111111-1111-1111-1111-111111111111";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "event-series-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _other = Path.Combine(Path.GetTempPath(), "event-series-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly SqliteEventSeriesRepository _repository;

    public SqliteEventSeriesRepositoryTests() => _repository = new SqliteEventSeriesRepository(_dir, _clock);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static EventSeries Series(string name = "Techorama", string? organiser = "Techorama BV", string? link = "https://techorama.nl") =>
        EventSeries.Create(Guid.NewGuid(), name, organiser, link).Value;

    [Fact]
    public async Task A_first_run_has_no_series()
    {
        Assert.Empty(await _repository.ListAsync(Cancellation));
        Assert.Null(await _repository.GetAsync(Guid.NewGuid(), Cancellation));
    }

    [Fact]
    public async Task A_fully_populated_series_reads_back_unchanged()
    {
        var stored = Series();

        await _repository.SaveAsync(stored, Cancellation);
        var read = await _repository.GetAsync(stored.Id, Cancellation);

        Assert.NotNull(read);
        Assert.Equal(stored.Id, read.Id);
        Assert.Equal("Techorama", read.Name);
        Assert.Equal("Techorama BV", read.Organiser);
        Assert.Equal("https://techorama.nl", read.Link);
    }

    [Fact]
    public async Task A_name_only_series_reads_back_with_no_organiser_or_link()
    {
        var stored = Series("dotNET Friday", organiser: null, link: null);

        await _repository.SaveAsync(stored, Cancellation);
        var read = await _repository.GetAsync(stored.Id, Cancellation);

        Assert.Equal("dotNET Friday", read!.Name);
        Assert.Null(read.Organiser);
        Assert.Null(read.Link);
    }

    [Fact]
    public async Task Saving_again_keeps_one_row_its_first_saved_stamp_and_a_later_updated_at()
    {
        var series = Series();
        await _repository.SaveAsync(series, Cancellation);

        _clock.SetUtcNow(Noon.AddMinutes(5));
        Assert.True(series.Rename("Techorama NL").IsSuccess);
        await _repository.SaveAsync(series, Cancellation);

        Assert.Equal("1", await ScalarAsync("SELECT COUNT(*) FROM events_series;"));
        Assert.Equal(Stamp(Noon), await ScalarAsync("SELECT created_at FROM events_series;"));
        Assert.Equal(Stamp(Noon.AddMinutes(5)), await ScalarAsync("SELECT updated_at FROM events_series;"));
        Assert.Equal("Techorama NL", (await _repository.GetAsync(series.Id, Cancellation))!.Name);
    }

    [Fact]
    public async Task The_list_holds_every_series_in_the_order_they_were_first_saved()
    {
        var first = Series("First");
        var second = Series("Second");

        await _repository.SaveAsync(first, Cancellation);
        _clock.SetUtcNow(Noon.AddMinutes(1));
        await _repository.SaveAsync(second, Cancellation);
        _clock.SetUtcNow(Noon.AddMinutes(2));
        await _repository.SaveAsync(first, Cancellation);

        Assert.Equal([first.Id, second.Id], (await _repository.ListAsync(Cancellation)).Select(series => series.Id));
    }

    /// <summary>Not JSON, a later shape, and a series with no name.</summary>
    [Theory]
    [InlineData("{not json")]
    [InlineData("""{"version":9,"id":"11111111-1111-1111-1111-111111111111","name":"Later shape"}""")]
    [InlineData("""{"version":1,"id":"11111111-1111-1111-1111-111111111111","name":" "}""")]
    public async Task A_row_that_does_not_read_as_a_series_is_skipped(string document)
    {
        var kept = Series();
        await _repository.SaveAsync(kept, Cancellation);
        await InsertRawAsync(BadId, document);

        Assert.Equal([kept.Id], (await _repository.ListAsync(Cancellation)).Select(series => series.Id));
        Assert.Null(await _repository.GetAsync(Guid.Parse(BadId), Cancellation));
    }

    /// <summary>The table is set up under its own stamp (local ADR 0006), and only its own: the
    /// events table is the event store's to create (inherited ADR 0014).</summary>
    [Fact]
    public async Task The_table_is_set_up_under_its_own_stamp_and_no_other()
    {
        await _repository.ListAsync(Cancellation);

        Assert.Equal("1", await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'events_series';"));
        Assert.Equal(SqliteEventSeriesRepository.Schema.Fingerprint, await ScalarAsync("SELECT fingerprint FROM schema_stamps WHERE owner = 'events_series';"));
        Assert.Equal("0", await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'events_knowledge_events';"));
    }

    [Fact]
    public async Task The_rooted_repository_follows_its_folder()
    {
        var root = _dir;
        var rooted = new RootedSqliteEventSeriesRepository(() => root);
        var series = Series();
        await rooted.SaveAsync(series, Cancellation);

        root = _other;

        Assert.Empty(await rooted.ListAsync(Cancellation));
        Assert.NotNull(await _repository.GetAsync(series.Id, Cancellation));
    }

    private static string Stamp(DateTimeOffset stamp) => stamp.ToString("O", CultureInfo.InvariantCulture);

    private async Task InsertRawAsync(string id, string document)
    {
        await using var connection = new SqliteConnection(SqliteSchema.ConnectionString(_repository.DatabasePath));
        await connection.OpenAsync(Cancellation);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO events_series (id, created_at, updated_at, document) VALUES ($id, '2026-01-01', '2026-01-01', $document);";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$document", document);
        await command.ExecuteNonQueryAsync(Cancellation);
    }

    private async Task<string> ScalarAsync(string sql)
    {
        await using var connection = new SqliteConnection(SqliteSchema.ConnectionString(_repository.DatabasePath));
        await connection.OpenAsync(Cancellation);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(Cancellation), CultureInfo.InvariantCulture)!;
    }

    /// <summary>Lets go of this test's own databases only — clearing every pool would reach the
    /// tests running beside it (see <c>SqlitePoolClearingTests</c>).</summary>
    public void Dispose()
    {
        foreach (var dir in new[] { _dir, _other })
        {
            SqliteDatabaseFile.ReleasePool(SqliteTaskRepository.DatabasePathFor(dir));

            if (!Directory.Exists(dir)) continue;
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
