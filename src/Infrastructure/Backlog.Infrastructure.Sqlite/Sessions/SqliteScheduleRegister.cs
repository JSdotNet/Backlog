using System.Globalization;
using System.Text.Json;

using Backlog.Modules.Sessions.Abstractions;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Sessions;

/// <summary>
/// The Sessions context's <see cref="IScheduleRegister"/> (local ADR 0051 §1): one row per
/// schedule in <c>schedules</c> and one per run in <c>scheduled_runs</c>, in the same
/// <c>backlog.db</c> the session requests live in. This machine's, and nothing replicates it.
/// <para>
/// A report is one immediate transaction: it reads the schedule and the run it names,
/// applies <see cref="ScheduleReports.Apply"/> and writes both, so a start and a finish
/// arriving together are serialized rather than one overwriting the other. Instants are
/// round-trip text in UTC; the Needs-you lines are a JSON array.
/// </para>
/// </summary>
public sealed class SqliteScheduleRegister : IScheduleRegister
{
    private const string ScheduleColumns = "id, title, repository, cron, first_reported_at, last_reported_at";

    private const string RunColumns =
        "session_id, schedule_id, first_reported_at, started_at, finished_at, verdict, needs_you, failed, skipped, held_by";

    private readonly string _databasePath;

    /// <summary>A register over the database in the given workspace folder.</summary>
    public SqliteScheduleRegister(string rootDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDir);

        _databasePath = SqliteTaskRepository.DatabasePathFor(rootDir);
    }

    /// <summary>The database file this register reads and writes.</summary>
    public string DatabasePath => _databasePath;

    public async Task<ScheduleRecord> RecordAsync(ScheduledRunReport report, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);

        var held = (await ReadRunsAsync(connection, transaction, null, report.SessionId, cancellationToken).ConfigureAwait(false)).FirstOrDefault();

        // A run keeps the schedule it first reported under: a session id is one run, and a
        // report naming another schedule does not rewrite the first one's title or cron.
        var applied = held is null || held.ScheduleId == report.ScheduleId
            ? report
            : report with { ScheduleId = held.ScheduleId, Title = null, Repository = null, Cron = null };
        var schedules = await ReadSchedulesAsync(connection, transaction, applied.ScheduleId, cancellationToken).ConfigureAwait(false);
        var (schedule, run) = ScheduleReports.Apply(schedules.FirstOrDefault(), held, applied, at);

        await WriteScheduleAsync(connection, transaction, schedule, cancellationToken).ConfigureAwait(false);
        await WriteRunAsync(connection, transaction, run, cancellationToken).ConfigureAwait(false);

        var all = await ReadRunsAsync(connection, transaction, schedule.Id, null, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new ScheduleRecord(schedule, all);
    }

    public async Task<IReadOnlyList<ScheduleRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);

        var schedules = await ReadSchedulesAsync(connection, null, null, cancellationToken).ConfigureAwait(false);
        var runs = await ReadRunsAsync(connection, null, null, null, cancellationToken).ConfigureAwait(false);
        var bySchedule = runs.ToLookup(run => run.ScheduleId, StringComparer.Ordinal);

        return [.. schedules.Select(schedule => new ScheduleRecord(schedule, [.. bySchedule[schedule.Id]]))];
    }

    private static async Task<IReadOnlyList<Schedule>> ReadSchedulesAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string? id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = id is null
            ? $"SELECT {ScheduleColumns} FROM schedules ORDER BY id;"
            : $"SELECT {ScheduleColumns} FROM schedules WHERE id = $id;";
        if (id is not null) command.Parameters.AddWithValue("$id", id);

        var schedules = new List<Schedule>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            schedules.Add(new Schedule(
                reader.GetString(0),
                Text(reader, 1),
                Text(reader, 2),
                Text(reader, 3),
                Instant(reader.GetString(4)),
                Instant(reader.GetString(5))));
        }

        return schedules;
    }

    private static async Task<IReadOnlyList<ScheduledRun>> ReadRunsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string? scheduleId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        var where = (scheduleId, sessionId) switch
        {
            (not null, _) => "WHERE schedule_id = $schedule_id",
            (_, not null) => "WHERE session_id = $session_id",
            _ => string.Empty,
        };

        command.CommandText = $"SELECT {RunColumns} FROM scheduled_runs {where} ORDER BY first_reported_at, session_id;";
        if (scheduleId is not null) command.Parameters.AddWithValue("$schedule_id", scheduleId);
        if (sessionId is not null) command.Parameters.AddWithValue("$session_id", sessionId);

        var runs = new List<ScheduledRun>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            runs.Add(new ScheduledRun(
                reader.GetString(0),
                reader.GetString(1),
                Instant(reader.GetString(2)),
                Text(reader, 3) is { } started ? Instant(started) : null,
                Text(reader, 4) is { } finished ? Instant(finished) : null,
                Text(reader, 5),
                Lines(Text(reader, 6)),
                reader.GetInt64(7) != 0,
                reader.GetInt64(8) != 0,
                Text(reader, 9)));
        }

        return runs;
    }

    private static async Task WriteScheduleAsync(SqliteConnection connection, SqliteTransaction transaction, Schedule schedule, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"INSERT OR REPLACE INTO schedules ({ScheduleColumns}) VALUES ($id, $title, $repository, $cron, $first, $last);";

        command.Parameters.AddWithValue("$id", schedule.Id);
        command.Parameters.AddWithValue("$title", (object?)schedule.Title ?? DBNull.Value);
        command.Parameters.AddWithValue("$repository", (object?)schedule.Repository ?? DBNull.Value);
        command.Parameters.AddWithValue("$cron", (object?)schedule.Cron ?? DBNull.Value);
        command.Parameters.AddWithValue("$first", Text(schedule.FirstReportedAt));
        command.Parameters.AddWithValue("$last", Text(schedule.LastReportedAt));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteRunAsync(SqliteConnection connection, SqliteTransaction transaction, ScheduledRun run, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            INSERT OR REPLACE INTO scheduled_runs ({RunColumns})
            VALUES ($session_id, $schedule_id, $first, $started, $finished, $verdict, $needs_you, $failed, $skipped, $held_by);
            """;

        command.Parameters.AddWithValue("$session_id", run.SessionId);
        command.Parameters.AddWithValue("$schedule_id", run.ScheduleId);
        command.Parameters.AddWithValue("$first", Text(run.FirstReportedAt));
        command.Parameters.AddWithValue("$started", run.StartedAt is { } started ? Text(started) : DBNull.Value);
        command.Parameters.AddWithValue("$finished", run.FinishedAt is { } finished ? Text(finished) : DBNull.Value);
        command.Parameters.AddWithValue("$verdict", (object?)run.Verdict ?? DBNull.Value);
        command.Parameters.AddWithValue("$needs_you", JsonSerializer.Serialize(run.NeedsYou));
        command.Parameters.AddWithValue("$failed", run.Failed ? 1 : 0);
        command.Parameters.AddWithValue("$skipped", run.Skipped ? 1 : 0);
        command.Parameters.AddWithValue("$held_by", (object?)run.HeldBy ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> Lines(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) is { } lines ? lines : [];
        }
        catch (JsonException)
        {
            // A value this build cannot read is no lines rather than a failed register.
            return [];
        }
    }

    private static string? Text(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static string Text(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Instant(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static Task<SqliteConnection> OpenAsync(string databasePath, CancellationToken cancellationToken) =>
        SqliteSchema.OpenAsync(databasePath, Schema, cancellationToken);

    internal static readonly SqliteSchemaDefinition Schema = new(
        owner: "schedules",
        ddl: """
            CREATE TABLE IF NOT EXISTS schedules (
                id                TEXT PRIMARY KEY NOT NULL,
                title             TEXT NULL,
                repository        TEXT NULL,
                cron              TEXT NULL,
                first_reported_at TEXT NOT NULL,
                last_reported_at  TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS scheduled_runs (
                session_id        TEXT PRIMARY KEY NOT NULL,
                schedule_id       TEXT NOT NULL,
                first_reported_at TEXT NOT NULL,
                started_at        TEXT NULL,
                finished_at       TEXT NULL,
                verdict           TEXT NULL,
                needs_you         TEXT NOT NULL,
                failed            INTEGER NOT NULL,
                skipped           INTEGER NOT NULL,
                held_by           TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_scheduled_runs_schedule ON scheduled_runs (schedule_id, first_reported_at);
            """);
}

/// <summary>
/// A <see cref="SqliteScheduleRegister"/> that follows a workspace root somebody can move
/// while the app is open — the arrangement <see cref="RootedSqliteSessionRequestStore"/> uses.
/// </summary>
public sealed class RootedSqliteScheduleRegister(Func<string> currentRootDirectory) : IScheduleRegister
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    private readonly Lock _gate = new();
    private string? _rootDirectory;
    private SqliteScheduleRegister? _register;

    private SqliteScheduleRegister Current
    {
        get
        {
            var root = _currentRootDirectory();

            lock (_gate)
            {
                if (_register is null || !string.Equals(_rootDirectory, root, StringComparison.OrdinalIgnoreCase))
                {
                    _rootDirectory = root;
                    _register = new SqliteScheduleRegister(root);
                }

                return _register;
            }
        }
    }

    public Task<ScheduleRecord> RecordAsync(ScheduledRunReport report, DateTimeOffset at, CancellationToken cancellationToken = default) =>
        Current.RecordAsync(report, at, cancellationToken);

    public Task<IReadOnlyList<ScheduleRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        Current.ListAsync(cancellationToken);
}
