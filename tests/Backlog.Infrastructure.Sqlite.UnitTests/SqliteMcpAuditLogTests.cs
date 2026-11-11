using Backlog.Infrastructure.Sqlite.Mcp;
using Backlog.SharedKernel.Mcp;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The MCP audit log's own database file (local ADR 0052, Phase 0): a record comes back
/// whole, newest first, at most the count asked for, and nothing older than thirty days
/// is kept or shown.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteMcpAuditLogTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-sqlite-mcp-audit", Guid.NewGuid().ToString("n"));

    private readonly FixedTime _time = new(Noon);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string DatabasePath => Path.Combine(_root, "mcp-audit.db");

    private static McpAuditRecord Call(string tool, DateTimeOffset at, McpCallOutcome outcome = McpCallOutcome.Succeeded, string? target = "4f1c") =>
        new(at, McpAudit.SharedTokenCaller, tool, target, outcome, "trace-" + tool);

    [Fact]
    public async Task A_record_comes_back_whole_in_a_file_of_its_own()
    {
        var log = new SqliteMcpAuditLog(DatabasePath, _time);
        var call = Call("read_item", Noon, McpCallOutcome.Refused, target: null);

        await log.AppendAsync(call, Ct);

        Assert.Equal([call], await log.LatestAsync(McpAudit.Shown, Ct));
        Assert.True(File.Exists(DatabasePath));
        Assert.False(File.Exists(Path.Combine(_root, "backlog.db")));
    }

    [Fact]
    public async Task The_latest_come_first_and_no_more_than_asked_for()
    {
        var log = new SqliteMcpAuditLog(DatabasePath, _time);

        await log.AppendAsync(Call("first", Noon.AddMinutes(-2)), Ct);
        await log.AppendAsync(Call("third", Noon), Ct);
        await log.AppendAsync(Call("second", Noon.AddMinutes(-1)), Ct);

        var latest = await log.LatestAsync(2, Ct);

        Assert.Equal(["third", "second"], latest.Select(call => call.Tool));
    }

    [Fact]
    public async Task A_record_older_than_thirty_days_is_neither_shown_nor_kept()
    {
        var log = new SqliteMcpAuditLog(DatabasePath, _time);
        await log.AppendAsync(Call("old", Noon.AddDays(-20)), Ct);

        // Ten days on, the first record is past its thirty: hidden at once, dropped by
        // the next write.
        _time.Now = Noon.AddDays(11);
        Assert.Empty(await log.LatestAsync(McpAudit.Shown, Ct));

        await log.AppendAsync(Call("new", _time.Now), Ct);

        Assert.Equal(["new"], (await log.LatestAsync(McpAudit.Shown, Ct)).Select(call => call.Tool));
        Assert.Equal(1L, await RowCountAsync());
    }

    [Fact]
    public async Task A_record_inside_thirty_days_is_kept()
    {
        var log = new SqliteMcpAuditLog(DatabasePath, _time);
        await log.AppendAsync(Call("recent", Noon.AddDays(-29)), Ct);
        await log.AppendAsync(Call("now", Noon), Ct);

        Assert.Equal(["now", "recent"], (await log.LatestAsync(McpAudit.Shown, Ct)).Select(call => call.Tool));
    }

    [Fact]
    public async Task Each_write_raises_changed()
    {
        var log = new SqliteMcpAuditLog(DatabasePath, _time);
        var raised = 0;
        log.Changed += () => raised++;

        await log.AppendAsync(Call("read_item", Noon), Ct);
        await log.AppendAsync(Call("comment", Noon), Ct);

        Assert.Equal(2, raised);
    }

    private async Task<long> RowCountAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM mcp_calls;";
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
