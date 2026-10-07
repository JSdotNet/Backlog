using System.Globalization;

using Backlog.Modules.Sessions.Abstractions;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Sessions;

/// <summary>
/// The Sessions context's <see cref="IClaudeApiRequestStore"/>: one row per
/// <c>claude_code.api_request</c> event in the <c>claude_api_requests</c> table of the
/// same <c>backlog.db</c> the tasks live in.
/// <para>
/// The table is this context's and no other adapter reads or creates it (inherited ADR
/// 0014); it is created by idempotent <c>IF NOT EXISTS</c> DDL on every open (local ADR
/// 0003), and a column added later goes through local ADR 0006's additive step.
/// </para>
/// <para>
/// <b>Idempotent by <c>request_id</c></b>, the primary key, through
/// <c>INSERT OR IGNORE</c>: the first copy of an event is the one kept, so an exporter
/// retrying a batch it never saw acknowledged changes nothing.
/// </para>
/// <para>
/// The cost is whole micro-dollars and the instant is round-trip text, the format the
/// tasks use, so a sum is exact and an instant reads back unchanged on any machine.
/// </para>
/// </summary>
public sealed class SqliteClaudeApiRequestStore : IClaudeApiRequestStore
{
    private const string Columns =
        "request_id, session_id, event_timestamp, model, effort, cost_usd_micros, input_tokens, output_tokens, " +
        "cache_read_tokens, cache_creation_tokens, duration_ms, query_source, agent_name, skill_name, prompt_id";

    private readonly string _databasePath;

    /// <summary>A store over the database in the given workspace folder — the same
    /// file <see cref="SqliteTaskRepository"/> uses.</summary>
    public SqliteClaudeApiRequestStore(string rootDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDir);

        _databasePath = SqliteTaskRepository.DatabasePathFor(rootDir);
    }

    /// <summary>The database file this store reads and writes.</summary>
    public string DatabasePath => _databasePath;

    public async Task<int> AddAsync(IReadOnlyList<ClaudeApiRequest> requests, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Count == 0) return 0;

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);

        // One transaction per batch: an export request is one delivery, and either all
        // of its rows are in or the exporter is told to send it again.
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            INSERT OR IGNORE INTO claude_api_requests ({Columns}, received_at)
            VALUES ($request_id, $session_id, $event_timestamp, $model, $effort, $cost_usd_micros, $input_tokens,
                    $output_tokens, $cache_read_tokens, $cache_creation_tokens, $duration_ms, $query_source,
                    $agent_name, $skill_name, $prompt_id, $received_at);
            """;

        var receivedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var added = 0;

        foreach (var request in requests)
        {
            ArgumentNullException.ThrowIfNull(request);

            command.Parameters.Clear();
            command.Parameters.AddWithValue("$request_id", request.RequestId);
            command.Parameters.AddWithValue("$session_id", (object?)request.SessionId ?? DBNull.Value);
            command.Parameters.AddWithValue("$event_timestamp", request.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$model", (object?)request.Model ?? DBNull.Value);
            command.Parameters.AddWithValue("$effort", (object?)request.Effort ?? DBNull.Value);
            command.Parameters.AddWithValue("$cost_usd_micros", (object?)request.CostUsdMicros ?? DBNull.Value);
            command.Parameters.AddWithValue("$input_tokens", (object?)request.InputTokens ?? DBNull.Value);
            command.Parameters.AddWithValue("$output_tokens", (object?)request.OutputTokens ?? DBNull.Value);
            command.Parameters.AddWithValue("$cache_read_tokens", (object?)request.CacheReadTokens ?? DBNull.Value);
            command.Parameters.AddWithValue("$cache_creation_tokens", (object?)request.CacheCreationTokens ?? DBNull.Value);
            command.Parameters.AddWithValue("$duration_ms", (object?)request.DurationMs ?? DBNull.Value);
            command.Parameters.AddWithValue("$query_source", (object?)request.QuerySource ?? DBNull.Value);
            command.Parameters.AddWithValue("$agent_name", (object?)request.AgentName ?? DBNull.Value);
            command.Parameters.AddWithValue("$skill_name", (object?)request.SkillName ?? DBNull.Value);
            command.Parameters.AddWithValue("$prompt_id", (object?)request.PromptId ?? DBNull.Value);
            command.Parameters.AddWithValue("$received_at", receivedAt);

            added += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return added;
    }

    public async Task<IReadOnlyList<ClaudeApiRequest>> ListAsync(string? sessionId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = sessionId is null
            ? $"SELECT {Columns} FROM claude_api_requests ORDER BY event_timestamp, request_id;"
            : $"SELECT {Columns} FROM claude_api_requests WHERE session_id = $session_id ORDER BY event_timestamp, request_id;";

        if (sessionId is not null) command.Parameters.AddWithValue("$session_id", sessionId);

        var requests = new List<ClaudeApiRequest>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            requests.Add(new ClaudeApiRequest(
                reader.GetString(0),
                Text(reader, 1),
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Text(reader, 3),
                Text(reader, 4),
                Number(reader, 5),
                Number(reader, 6),
                Number(reader, 7),
                Number(reader, 8),
                Number(reader, 9),
                Number(reader, 10),
                Text(reader, 11),
                Text(reader, 12),
                Text(reader, 13),
                Text(reader, 14)));
        }

        return requests;
    }

    private static string? Text(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static long? Number(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static Task<SqliteConnection> OpenAsync(string databasePath, CancellationToken cancellationToken) =>
        SqliteSchema.OpenAsync(databasePath, EnsureSchemaAsync, cancellationToken);

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await SqliteSchema.EnsureAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS claude_api_requests (
                request_id            TEXT PRIMARY KEY NOT NULL,
                session_id            TEXT NULL,
                event_timestamp       TEXT NOT NULL,
                model                 TEXT NULL,
                effort                TEXT NULL,
                cost_usd_micros       INTEGER NULL,
                input_tokens          INTEGER NULL,
                output_tokens         INTEGER NULL,
                cache_read_tokens     INTEGER NULL,
                cache_creation_tokens INTEGER NULL,
                duration_ms           INTEGER NULL,
                query_source          TEXT NULL,
                agent_name            TEXT NULL,
                skill_name            TEXT NULL,
                prompt_id             TEXT NULL,
                received_at           TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_claude_api_requests_session ON claude_api_requests (session_id, event_timestamp);
            """,
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// A <see cref="SqliteClaudeApiRequestStore"/> that follows a workspace root somebody
/// can move while the app is open — the arrangement
/// <c>RootedSqliteInboxRepository</c> uses, over the same file.
/// </summary>
public sealed class RootedSqliteClaudeApiRequestStore(Func<string> currentRootDirectory) : IClaudeApiRequestStore
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    private readonly Lock _gate = new();
    private string? _rootDirectory;
    private SqliteClaudeApiRequestStore? _store;

    /// <summary>The database the store is pointed at right now.</summary>
    public string DatabasePath => Current.DatabasePath;

    private SqliteClaudeApiRequestStore Current
    {
        get
        {
            var root = _currentRootDirectory();

            lock (_gate)
            {
                if (_store is null || !string.Equals(_rootDirectory, root, StringComparison.OrdinalIgnoreCase))
                {
                    _rootDirectory = root;
                    _store = new SqliteClaudeApiRequestStore(root);
                }

                return _store;
            }
        }
    }

    public Task<int> AddAsync(IReadOnlyList<ClaudeApiRequest> requests, CancellationToken cancellationToken = default) =>
        Current.AddAsync(requests, cancellationToken);

    public Task<IReadOnlyList<ClaudeApiRequest>> ListAsync(string? sessionId = null, CancellationToken cancellationToken = default) =>
        Current.ListAsync(sessionId, cancellationToken);
}
