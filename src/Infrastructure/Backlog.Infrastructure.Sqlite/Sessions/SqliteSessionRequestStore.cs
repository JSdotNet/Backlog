using System.Globalization;

using Backlog.Modules.Sessions.Abstractions;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Sessions;

/// <summary>
/// The Sessions context's <see cref="ISessionRequestStore"/> (local ADR 0038): one row
/// per session request in the <c>session_requests</c> table of the same
/// <c>backlog.db</c> the <c>claude_api_requests</c> live in. The queue is this
/// machine's — a request names a clone folder here — and nothing replicates it.
/// <para>
/// The table is this context's, created by idempotent <c>IF NOT EXISTS</c> DDL on any
/// open the file is not yet stamped for (local ADR 0003).
/// </para>
/// <para>
/// <b>Every operation is one transaction</b> that reads the rows it decides on,
/// applies the pure transition from <see cref="SessionRequests"/> and writes the
/// result. The transaction is SQLite's immediate kind, so it holds the write lock from
/// its first read: two dispatchers claiming one request are serialized, and the second
/// reads the first's claim and is refused.
/// </para>
/// <para>
/// The state is stored as written; a lapsed lease is not rewritten when it lapses.
/// Every read answers through <see cref="SessionRequest.StateAt"/>, so a lapsed claim
/// reads as Requested everywhere without a sweeper. Instants are round-trip text in
/// UTC, the format the tasks use.
/// </para>
/// </summary>
public sealed class SqliteSessionRequestStore : ISessionRequestStore
{
    private const string Columns =
        "id, kind, entry_id, plan_tag, repository, clone_folder, state, claimed_by, lease_ends_at, session_id, created_at";

    private readonly string _databasePath;
    private readonly TimeProvider _clock;

    /// <summary>A store over the database in the given workspace folder.</summary>
    /// <param name="rootDir">The workspace folder holding <c>backlog.db</c>.</param>
    /// <param name="clock">Where "now" comes from, for the lease; the system clock when null.</param>
    public SqliteSessionRequestStore(string rootDir, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDir);

        _databasePath = SqliteTaskRepository.DatabasePathFor(rootDir);
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The database file this store reads and writes.</summary>
    public string DatabasePath => _databasePath;

    public async Task<SessionRequest> RequestAsync(SessionRequest candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var transaction = await BeginAsync(connection, cancellationToken).ConfigureAwait(false);

        var held = await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        if (SessionRequests.OpenDuplicateOf(candidate, held, Now) is { } open)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return open;
        }

        await WriteAsync(connection, transaction, candidate, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return candidate;
    }

    public async Task<SessionRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var all = await ListAsync(null, cancellationToken).ConfigureAwait(false);

        return all.FirstOrDefault(request => request.Id == id);
    }

    public async Task<IReadOnlyList<SessionRequest>> ListAsync(SessionRequestState? state = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);

        var held = await ReadAsync(connection, null, cancellationToken).ConfigureAwait(false);
        var now = Now;

        return
        [
            .. held
                .Where(request => state is null || request.StateAt(now) == state)
                .OrderBy(request => request.CreatedAt)
                .ThenBy(request => request.Id)
        ];
    }

    public async Task<SessionRequest?> NextAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);

        var held = await ReadAsync(connection, null, cancellationToken).ConfigureAwait(false);

        return SessionRequests.Waiting(held, Now).FirstOrDefault();
    }

    public Task<SessionRequestOutcome> ClaimAsync(Guid id, string dispatcher, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, (request, now) => SessionRequests.Claim(request, dispatcher, now), cancellationToken);

    public Task<SessionRequestOutcome> ReleaseAsync(Guid id, string dispatcher, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, (request, now) => SessionRequests.Release(request, dispatcher, now), cancellationToken);

    public Task<SessionRequestOutcome> ReportStartedAsync(Guid id, string dispatcher, string sessionId, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, (request, now) => SessionRequests.Report(request, dispatcher, sessionId, now), cancellationToken);

    public Task<SessionRequestOutcome> CancelAsync(Guid id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, SessionRequests.Cancel, cancellationToken);

    public async Task<IReadOnlyList<SessionRequest>> LinkAsync(
        Guid entryId,
        IReadOnlyCollection<string> planTags,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planTags);

        if (string.IsNullOrWhiteSpace(sessionId)) return [];

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var transaction = await BeginAsync(connection, cancellationToken).ConfigureAwait(false);

        var now = Now;
        var linked = new List<SessionRequest>();

        foreach (var request in await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
        {
            if (SessionRequests.Link(request, entryId, planTags, sessionId, now) is not { } changed) continue;

            await WriteAsync(connection, transaction, changed, cancellationToken).ConfigureAwait(false);
            linked.Add(changed);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return linked;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    private async Task<SessionRequestOutcome> ChangeAsync(
        Guid id,
        Func<SessionRequest, DateTimeOffset, SessionRequestOutcome> transition,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var transaction = await BeginAsync(connection, cancellationToken).ConfigureAwait(false);

        var held = await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        if (held.FirstOrDefault(request => request.Id == id) is not { } request)
        {
            return SessionRequestOutcome.Refused(null, $"No session request has the id '{id}'.");
        }

        var outcome = transition(request, Now);

        if (outcome is { Succeeded: true, Request: { } changed })
        {
            await WriteAsync(connection, transaction, changed, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return outcome;
    }

    private static Task<SqliteTransaction> BeginAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Not deferred: BEGIN IMMEDIATE takes the write lock before the read, so the
        // read a transition decides on cannot be overtaken by another writer.
        return Task.FromResult(connection.BeginTransaction(deferred: false));
    }

    private static async Task<IReadOnlyList<SessionRequest>> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {Columns} FROM session_requests ORDER BY created_at, id;";

        var requests = new List<SessionRequest>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // A row this build cannot read — a kind or state a later build added — is
            // left out rather than failing the queue; nothing here rewrites it.
            if (!Named<SessionRequestKind>(reader.GetString(1), out var kind)) continue;
            if (!Named<SessionRequestState>(reader.GetString(6), out var state)) continue;
            if (!Guid.TryParse(reader.GetString(0), out var id)) continue;

            requests.Add(new SessionRequest(
                id,
                kind,
                Text(reader, 2) is { } entry && Guid.TryParse(entry, out var entryId) ? entryId : null,
                Text(reader, 3),
                reader.GetString(4),
                reader.GetString(5),
                state,
                Text(reader, 7),
                Instant(Text(reader, 8)),
                Text(reader, 9),
                Instant(reader.GetString(10))!.Value));
        }

        return requests;
    }

    private static async Task WriteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            INSERT OR REPLACE INTO session_requests ({Columns})
            VALUES ($id, $kind, $entry_id, $plan_tag, $repository, $clone_folder, $state, $claimed_by,
                    $lease_ends_at, $session_id, $created_at);
            """;

        command.Parameters.AddWithValue("$id", request.Id.ToString("D"));
        command.Parameters.AddWithValue("$kind", request.Kind.ToString());
        command.Parameters.AddWithValue("$entry_id", (object?)request.EntryId?.ToString("D") ?? DBNull.Value);
        command.Parameters.AddWithValue("$plan_tag", (object?)request.PlanTag ?? DBNull.Value);
        command.Parameters.AddWithValue("$repository", request.Repository);
        command.Parameters.AddWithValue("$clone_folder", request.CloneFolder);
        command.Parameters.AddWithValue("$state", request.State.ToString());
        command.Parameters.AddWithValue("$claimed_by", (object?)request.ClaimedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("$lease_ends_at", (object?)Text(request.LeaseEndsAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$session_id", (object?)request.SessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", Text(request.CreatedAt)!);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A member written by name. Numeric text, which <see cref="Enum.TryParse{TEnum}(string, out TEnum)"/>
    /// would also accept, is no name this store writes.</summary>
    private static bool Named<TEnum>(string text, out TEnum value) where TEnum : struct, Enum =>
        Enum.TryParse(text, ignoreCase: false, out value) && Enum.IsDefined(value) && !char.IsAsciiDigit(text.FirstOrDefault());

    private static string? Text(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static string? Text(DateTimeOffset? instant) =>
        instant?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? Instant(string? text) =>
        text is null ? null : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static Task<SqliteConnection> OpenAsync(string databasePath, CancellationToken cancellationToken) =>
        SqliteSchema.OpenAsync(databasePath, Schema, cancellationToken);

    internal static readonly SqliteSchemaDefinition Schema = new(
        owner: "session_requests",
        ddl: """
            CREATE TABLE IF NOT EXISTS session_requests (
                id            TEXT PRIMARY KEY NOT NULL,
                kind          TEXT NOT NULL,
                entry_id      TEXT NULL,
                plan_tag      TEXT NULL,
                repository    TEXT NOT NULL,
                clone_folder  TEXT NOT NULL,
                state         TEXT NOT NULL,
                claimed_by    TEXT NULL,
                lease_ends_at TEXT NULL,
                session_id    TEXT NULL,
                created_at    TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_session_requests_created ON session_requests (created_at, id);
            """);
}

/// <summary>
/// A <see cref="SqliteSessionRequestStore"/> that follows a workspace root somebody can
/// move while the app is open — the arrangement <c>RootedSqliteClaudeApiRequestStore</c>
/// uses, over the same file.
/// </summary>
public sealed class RootedSqliteSessionRequestStore(Func<string> currentRootDirectory, TimeProvider? clock = null) : ISessionRequestStore
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    private readonly Lock _gate = new();
    private string? _rootDirectory;
    private SqliteSessionRequestStore? _store;

    /// <summary>The database the store is pointed at right now.</summary>
    public string DatabasePath => Current.DatabasePath;

    private SqliteSessionRequestStore Current
    {
        get
        {
            var root = _currentRootDirectory();

            lock (_gate)
            {
                if (_store is null || !string.Equals(_rootDirectory, root, StringComparison.OrdinalIgnoreCase))
                {
                    _rootDirectory = root;
                    _store = new SqliteSessionRequestStore(root, clock);
                }

                return _store;
            }
        }
    }

    public Task<SessionRequest> RequestAsync(SessionRequest candidate, CancellationToken cancellationToken = default) =>
        Current.RequestAsync(candidate, cancellationToken);

    public Task<SessionRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<SessionRequest>> ListAsync(SessionRequestState? state = null, CancellationToken cancellationToken = default) =>
        Current.ListAsync(state, cancellationToken);

    public Task<SessionRequest?> NextAsync(CancellationToken cancellationToken = default) =>
        Current.NextAsync(cancellationToken);

    public Task<SessionRequestOutcome> ClaimAsync(Guid id, string dispatcher, CancellationToken cancellationToken = default) =>
        Current.ClaimAsync(id, dispatcher, cancellationToken);

    public Task<SessionRequestOutcome> ReleaseAsync(Guid id, string dispatcher, CancellationToken cancellationToken = default) =>
        Current.ReleaseAsync(id, dispatcher, cancellationToken);

    public Task<SessionRequestOutcome> ReportStartedAsync(Guid id, string dispatcher, string sessionId, CancellationToken cancellationToken = default) =>
        Current.ReportStartedAsync(id, dispatcher, sessionId, cancellationToken);

    public Task<SessionRequestOutcome> CancelAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.CancelAsync(id, cancellationToken);

    public Task<IReadOnlyList<SessionRequest>> LinkAsync(
        Guid entryId,
        IReadOnlyCollection<string> planTags,
        string sessionId,
        CancellationToken cancellationToken = default) =>
        Current.LinkAsync(entryId, planTags, sessionId, cancellationToken);
}
