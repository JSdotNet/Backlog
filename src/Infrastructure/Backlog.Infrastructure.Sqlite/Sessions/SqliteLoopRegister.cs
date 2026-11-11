using System.Globalization;

using Backlog.Modules.Sessions.Abstractions;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Sessions;

/// <summary>
/// The Sessions context's <see cref="ILoopRegister"/> (local ADR 0051 §3): one row per loop
/// session in the <c>loop_check_ins</c> table of the same <c>backlog.db</c> the session
/// requests live in — its latest check-in and the command the person last set. This
/// machine's; nothing replicates it.
/// <para>
/// The table is this context's, created by idempotent <c>IF NOT EXISTS</c> DDL on any open
/// the file is not yet stamped for (local ADR 0003). A check-in, a dispatch wait and a
/// command each write in one immediate transaction, so a check-in arriving while the person
/// sets a command keeps the command. A dispatch wait writes the check-in time and nothing else.
/// </para>
/// </summary>
public sealed class SqliteLoopRegister : ILoopRegister
{
    private const string Columns =
        "session_id, kind, activity, heartbeat_minutes, held_requests, held_deliveries, held_sessions, held_notes, "
        + "checked_in_at, command, command_set_at";

    private readonly string _databasePath;
    private readonly TimeProvider _clock;

    /// <summary>A register over the database in the given workspace folder.</summary>
    /// <param name="rootDir">The workspace folder holding <c>backlog.db</c>.</param>
    /// <param name="clock">Where "now" comes from, for when a command is set; the system clock when null.</param>
    public SqliteLoopRegister(string rootDir, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDir);

        _databasePath = SqliteTaskRepository.DatabasePathFor(rootDir);
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The database file this register reads and writes.</summary>
    public string DatabasePath => _databasePath;

    public async Task<LoopSession> CheckInAsync(LoopCheckIn checkIn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkIn);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);

        var earlier = await ReadAsync(connection, transaction, checkIn.SessionId, cancellationToken).ConfigureAwait(false);
        var recorded = LoopSessions.Record(earlier, checkIn);

        // An upsert of the check-in's columns only: a row a later build wrote, which this
        // build reads as null, keeps the command the person set on it.
        await WriteAsync(connection, transaction, recorded, keepCommand: true, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return recorded;
    }

    public async Task<LoopSession?> RecordWaitAsync(string sessionId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return null;

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);

        if (await ReadAsync(connection, transaction, sessionId.Trim(), cancellationToken).ConfigureAwait(false) is not { } loop)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return null;
        }

        var waited = LoopSessions.RecordWait(loop, at);

        // The check-in time alone: a column a later build added keeps what that build wrote.
        if (waited != loop)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE loop_check_ins SET checked_in_at = $checked_in_at WHERE session_id = $session_id;";
            update.Parameters.AddWithValue("$checked_in_at", Text(waited.CheckIn.At)!);
            update.Parameters.AddWithValue("$session_id", waited.SessionId);

            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return waited;
    }

    public async Task<LoopSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return null;

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        return await ReadAsync(connection, null, sessionId.Trim(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LoopSession>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM loop_check_ins;";

        var loops = new List<LoopSession>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Row(reader) is { } loop) loops.Add(loop);
        }

        return [.. loops.OrderByDescending(loop => loop.CheckIn.At).ThenBy(loop => loop.SessionId, StringComparer.Ordinal)];
    }

    public async Task<LoopSession?> SetCommandAsync(string sessionId, LoopCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return null;

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);

        if (await ReadAsync(connection, transaction, sessionId.Trim(), cancellationToken).ConfigureAwait(false) is not { } loop)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return null;
        }

        var changed = LoopSessions.Command(loop, command, _clock.GetUtcNow());

        await WriteAsync(connection, transaction, changed, keepCommand: false, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return changed;
    }

    private Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken) =>
        SqliteSchema.OpenAsync(_databasePath, Schema, cancellationToken);

    private static async Task<LoopSession?> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sessionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {Columns} FROM loop_check_ins WHERE session_id = $session_id;";
        command.Parameters.AddWithValue("$session_id", sessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Row(reader) : null;
    }

    /// <summary>A row this build can read, or null: a kind, activity or command a later
    /// build added is left out rather than failing the register.</summary>
    private static LoopSession? Row(SqliteDataReader reader)
    {
        if (!Named<LoopKind>(reader.GetString(1), out var kind)) return null;
        if (!Named<LoopActivity>(reader.GetString(2), out var activity)) return null;
        if (!Named<LoopCommand>(reader.GetString(9), out var command)) return null;

        var checkIn = new LoopCheckIn(
            reader.GetString(0),
            kind,
            activity,
            reader.GetInt32(3),
            new LoopHeldWork(reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7)),
            Instant(reader.GetString(8))!.Value);

        return new LoopSession(checkIn, command, Instant(reader.IsDBNull(10) ? null : reader.GetString(10)));
    }

    private static async Task WriteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LoopSession loop,
        bool keepCommand,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        var commandColumns = keepCommand
            ? string.Empty
            : ", command = excluded.command, command_set_at = excluded.command_set_at";

        command.CommandText =
            $"""
            INSERT INTO loop_check_ins ({Columns})
            VALUES ($session_id, $kind, $activity, $heartbeat_minutes, $held_requests, $held_deliveries,
                    $held_sessions, $held_notes, $checked_in_at, $command, $command_set_at)
            ON CONFLICT (session_id) DO UPDATE SET
                kind = excluded.kind, activity = excluded.activity, heartbeat_minutes = excluded.heartbeat_minutes,
                held_requests = excluded.held_requests, held_deliveries = excluded.held_deliveries,
                held_sessions = excluded.held_sessions, held_notes = excluded.held_notes,
                checked_in_at = excluded.checked_in_at{commandColumns};
            """;

        var checkIn = loop.CheckIn;

        command.Parameters.AddWithValue("$session_id", checkIn.SessionId);
        command.Parameters.AddWithValue("$kind", checkIn.Kind.ToString());
        command.Parameters.AddWithValue("$activity", checkIn.Activity.ToString());
        command.Parameters.AddWithValue("$heartbeat_minutes", checkIn.HeartbeatMinutes);
        command.Parameters.AddWithValue("$held_requests", checkIn.Held.Requests);
        command.Parameters.AddWithValue("$held_deliveries", checkIn.Held.Deliveries);
        command.Parameters.AddWithValue("$held_sessions", checkIn.Held.Sessions);
        command.Parameters.AddWithValue("$held_notes", checkIn.Held.Notes);
        command.Parameters.AddWithValue("$checked_in_at", Text(checkIn.At)!);
        command.Parameters.AddWithValue("$command", loop.Command.ToString());
        command.Parameters.AddWithValue("$command_set_at", (object?)Text(loop.CommandSetAt) ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A member written by name; numeric text is no name this register writes.</summary>
    private static bool Named<TEnum>(string text, out TEnum value) where TEnum : struct, Enum =>
        Enum.TryParse(text, ignoreCase: false, out value) && Enum.IsDefined(value) && !char.IsAsciiDigit(text.FirstOrDefault());

    private static string? Text(DateTimeOffset? instant) =>
        instant?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? Instant(string? text) =>
        text is null ? null : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    internal static readonly SqliteSchemaDefinition Schema = new(
        owner: "loop_check_ins",
        ddl: """
            CREATE TABLE IF NOT EXISTS loop_check_ins (
                session_id        TEXT PRIMARY KEY NOT NULL,
                kind              TEXT NOT NULL,
                activity          TEXT NOT NULL,
                heartbeat_minutes INTEGER NOT NULL,
                held_requests     INTEGER NOT NULL,
                held_deliveries   INTEGER NOT NULL,
                held_sessions     INTEGER NOT NULL,
                held_notes        INTEGER NOT NULL,
                checked_in_at     TEXT NOT NULL,
                command           TEXT NOT NULL,
                command_set_at    TEXT NULL
            );
            """);
}

/// <summary>
/// A <see cref="SqliteLoopRegister"/> that follows a workspace root somebody can move while
/// the app is open — the arrangement <see cref="RootedSqliteSessionRequestStore"/> uses, over
/// the same file.
/// </summary>
public sealed class RootedSqliteLoopRegister(Func<string> currentRootDirectory, TimeProvider? clock = null) : ILoopRegister
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    private readonly Lock _gate = new();
    private string? _rootDirectory;
    private SqliteLoopRegister? _register;

    private SqliteLoopRegister Current
    {
        get
        {
            var root = _currentRootDirectory();

            lock (_gate)
            {
                if (_register is null || !string.Equals(_rootDirectory, root, StringComparison.OrdinalIgnoreCase))
                {
                    _rootDirectory = root;
                    _register = new SqliteLoopRegister(root, clock);
                }

                return _register;
            }
        }
    }

    public Task<LoopSession> CheckInAsync(LoopCheckIn checkIn, CancellationToken cancellationToken = default) =>
        Current.CheckInAsync(checkIn, cancellationToken);

    public Task<LoopSession?> RecordWaitAsync(string sessionId, DateTimeOffset at, CancellationToken cancellationToken = default) =>
        Current.RecordWaitAsync(sessionId, at, cancellationToken);

    public Task<LoopSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default) =>
        Current.GetAsync(sessionId, cancellationToken);

    public Task<IReadOnlyList<LoopSession>> ListAsync(CancellationToken cancellationToken = default) =>
        Current.ListAsync(cancellationToken);

    public Task<LoopSession?> SetCommandAsync(string sessionId, LoopCommand command, CancellationToken cancellationToken = default) =>
        Current.SetCommandAsync(sessionId, command, cancellationToken);
}
