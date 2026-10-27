using System.Globalization;
using System.Text.Json;

using Backlog.Modules.Sessions.Abstractions;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Sessions;

/// <summary>
/// The Sessions context's <see cref="IWaitingQuestionStore"/> (local ADR 0041): one row per
/// waiting question in the <c>waiting_questions</c> table of the same <c>backlog.db</c> the
/// <c>session_requests</c> live in. The questions are this machine's — presence is read
/// here — and nothing replicates them.
/// <para>
/// The table is this context's, created by idempotent <c>IF NOT EXISTS</c> DDL on any open
/// the file is not yet stamped for (local ADR 0003). The questions, the notification ids
/// and the answers are JSON text: they are read and written whole, never queried.
/// </para>
/// <para>
/// <b>Every write is one immediate transaction</b> that reads the rows it decides on,
/// applies the pure transition from <see cref="WaitingQuestions"/> and writes the result,
/// so two hooks registering one dialog are serialized and the second gets the first's
/// question back. Instants are round-trip text in UTC.
/// </para>
/// </summary>
public sealed class SqliteWaitingQuestionStore : IWaitingQuestionStore
{
    private const string Columns =
        "id, session_id, tool_use_id, kind, route, state, questions, notification_ids, answers, " +
        "asked_at, escalate_at, hook_polled_at, delivery, close_reason, ended_at";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _databasePath;

    /// <summary>A store over the database in the given workspace folder.</summary>
    /// <param name="rootDir">The workspace folder holding <c>backlog.db</c>.</param>
    public SqliteWaitingQuestionStore(string rootDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDir);

        _databasePath = SqliteTaskRepository.DatabasePathFor(rootDir);
    }

    /// <summary>The database file this store reads and writes.</summary>
    public string DatabasePath => _databasePath;

    public async Task<WaitingQuestion> RegisterAsync(WaitingQuestion candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var transaction = await BeginAsync(connection, cancellationToken).ConfigureAwait(false);

        if (candidate.ToolUseId is not null)
        {
            var held = await ReadAsync(connection, transaction, "WHERE session_id = $session AND tool_use_id = $tool_use",
                command =>
                {
                    command.Parameters.AddWithValue("$session", candidate.SessionId);
                    command.Parameters.AddWithValue("$tool_use", candidate.ToolUseId);
                }, cancellationToken).ConfigureAwait(false);

            if (WaitingQuestions.DuplicateOf(candidate, held) is { } registered)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return registered;
            }
        }

        await WriteAsync(connection, transaction, candidate, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return candidate;
    }

    public async Task<WaitingQuestion?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);

        var held = await ReadAsync(connection, null, "WHERE id = $id",
            command => command.Parameters.AddWithValue("$id", id.ToString("D")), cancellationToken).ConfigureAwait(false);

        return held.FirstOrDefault();
    }

    public async Task<IReadOnlyList<WaitingQuestion>> ListAsync(bool openOnly = false, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);

        return await ReadAsync(
            connection,
            null,
            openOnly ? $"WHERE state IN ('{nameof(WaitingQuestionState.InSession)}', '{nameof(WaitingQuestionState.Notified)}')" : string.Empty,
            null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<WaitingQuestionOutcome> ChangeAsync(
        Guid id,
        Func<WaitingQuestion, WaitingQuestionOutcome> transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var transaction = await BeginAsync(connection, cancellationToken).ConfigureAwait(false);

        var held = await ReadAsync(connection, transaction, "WHERE id = $id",
            command => command.Parameters.AddWithValue("$id", id.ToString("D")), cancellationToken).ConfigureAwait(false);

        if (held.FirstOrDefault() is not { } question)
        {
            return WaitingQuestionOutcome.Refused(null, $"No waiting question has the id '{id:D}'.");
        }

        var outcome = transition(question);

        if (outcome is { Succeeded: true, Question: { } changed })
        {
            await WriteAsync(connection, transaction, changed, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return outcome;
    }

    private static Task<SqliteTransaction> BeginAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Not deferred: BEGIN IMMEDIATE takes the write lock before the read, so the read a
        // transition decides on cannot be overtaken by another writer.
        return Task.FromResult(connection.BeginTransaction(deferred: false));
    }

    private static async Task<IReadOnlyList<WaitingQuestion>> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string where,
        Action<SqliteCommand>? bind,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {Columns} FROM waiting_questions {where} ORDER BY asked_at, id;";
        bind?.Invoke(command);

        var questions = new List<WaitingQuestion>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // A row this build cannot read — a state a later build added — is left out
            // rather than failing the relay; nothing here rewrites it.
            if (!Guid.TryParse(reader.GetString(0), out var id)) continue;
            if (!Named<WaitingQuestionKind>(reader.GetString(3), out var kind)) continue;
            if (!Named<WaitingQuestionRoute>(reader.GetString(4), out var route)) continue;
            if (!Named<WaitingQuestionState>(reader.GetString(5), out var state)) continue;

            AnswerDeliveryState? delivery = null;
            if (Text(reader, 12) is { } deliveryText)
            {
                if (!Named<AnswerDeliveryState>(deliveryText, out var parsed)) continue;
                delivery = parsed;
            }

            WaitingQuestionCloseReason? reason = null;
            if (Text(reader, 13) is { } reasonText)
            {
                if (!Named<WaitingQuestionCloseReason>(reasonText, out var parsed)) continue;
                reason = parsed;
            }

            List<StoredQuestion> asked;
            List<Guid> notified;
            List<StoredAnswer?> answers;

            try
            {
                asked = JsonSerializer.Deserialize<List<StoredQuestion>>(reader.GetString(6), Json) ?? [];
                notified = JsonSerializer.Deserialize<List<Guid>>(reader.GetString(7), Json) ?? [];
                answers = JsonSerializer.Deserialize<List<StoredAnswer?>>(reader.GetString(8), Json) ?? [];
            }
            catch (JsonException)
            {
                // A row this build cannot read is left out, as an unknown name is.
                continue;
            }

            questions.Add(new WaitingQuestion(
                id,
                reader.GetString(1),
                Text(reader, 2),
                kind,
                route,
                state,
                [.. asked.Select(question => question.ToQuestion())],
                notified,
                [.. answers.Select(answer => answer?.ToAnswer())],
                Instant(reader.GetString(9))!.Value,
                Instant(reader.GetString(10))!.Value,
                Instant(Text(reader, 11)),
                delivery,
                reason,
                Instant(Text(reader, 14))));
        }

        return questions;
    }

    private static async Task WriteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        WaitingQuestion question,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            INSERT OR REPLACE INTO waiting_questions ({Columns})
            VALUES ($id, $session_id, $tool_use_id, $kind, $route, $state, $questions, $notification_ids, $answers,
                    $asked_at, $escalate_at, $hook_polled_at, $delivery, $close_reason, $ended_at);
            """;

        command.Parameters.AddWithValue("$id", question.Id.ToString("D"));
        command.Parameters.AddWithValue("$session_id", question.SessionId);
        command.Parameters.AddWithValue("$tool_use_id", (object?)question.ToolUseId ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", question.Kind.ToString());
        command.Parameters.AddWithValue("$route", question.Route.ToString());
        command.Parameters.AddWithValue("$state", question.State.ToString());
        command.Parameters.AddWithValue("$questions", JsonSerializer.Serialize(question.Questions.Select(StoredQuestion.From), Json));
        command.Parameters.AddWithValue("$notification_ids", JsonSerializer.Serialize(question.NotificationIds, Json));
        command.Parameters.AddWithValue("$answers", JsonSerializer.Serialize(question.Answers.Select(StoredAnswer.From), Json));
        command.Parameters.AddWithValue("$asked_at", Text(question.AskedAt)!);
        command.Parameters.AddWithValue("$escalate_at", Text(question.EscalateAt)!);
        command.Parameters.AddWithValue("$hook_polled_at", (object?)Text(question.HookPolledAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$delivery", (object?)question.Delivery?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$close_reason", (object?)question.CloseReason?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$ended_at", (object?)Text(question.EndedAt) ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A member written by name. Numeric text is no name this store writes.</summary>
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
        owner: "waiting_questions",
        ddl: """
            CREATE TABLE IF NOT EXISTS waiting_questions (
                id               TEXT PRIMARY KEY NOT NULL,
                session_id       TEXT NOT NULL,
                tool_use_id      TEXT NULL,
                kind             TEXT NOT NULL,
                route            TEXT NOT NULL,
                state            TEXT NOT NULL,
                questions        TEXT NOT NULL,
                notification_ids TEXT NOT NULL,
                answers          TEXT NOT NULL,
                asked_at         TEXT NOT NULL,
                escalate_at      TEXT NOT NULL,
                hook_polled_at   TEXT NULL,
                delivery         TEXT NULL,
                close_reason     TEXT NULL,
                ended_at         TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_waiting_questions_tool_use ON waiting_questions (session_id, tool_use_id);
            CREATE INDEX IF NOT EXISTS ix_waiting_questions_state ON waiting_questions (state, escalate_at);
            """);

    private sealed record StoredOption(string Label, string? Description);

    private sealed record StoredQuestion(string Question, string? Header, List<StoredOption>? Options, bool MultiSelect)
    {
        public static StoredQuestion From(RelayQuestion question) =>
            new(question.Question, question.Header, [.. question.Choices.Select(option => new StoredOption(option.Label, option.Description))], question.MultiSelect);

        public RelayQuestion ToQuestion() =>
            new(Question, Header, [.. (Options ?? []).Select(option => new RelayOption(option.Label, option.Description))], MultiSelect);
    }

    private sealed record StoredAnswer(List<string>? Choices, string? Text)
    {
        public static StoredAnswer? From(RelayAnswer? answer) =>
            answer is null ? null : new StoredAnswer([.. answer.Choices], answer.Text);

        public RelayAnswer ToAnswer() => new([.. Choices ?? []], Text);
    }
}

/// <summary>
/// A <see cref="SqliteWaitingQuestionStore"/> that follows a workspace root somebody can
/// move while the app is open — the arrangement <see cref="RootedSqliteSessionRequestStore"/>
/// uses, over the same file.
/// </summary>
public sealed class RootedSqliteWaitingQuestionStore(Func<string> currentRootDirectory) : IWaitingQuestionStore
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    private readonly Lock _gate = new();
    private string? _rootDirectory;
    private SqliteWaitingQuestionStore? _store;

    /// <summary>The database the store is pointed at right now.</summary>
    public string DatabasePath => Current.DatabasePath;

    private SqliteWaitingQuestionStore Current
    {
        get
        {
            var root = _currentRootDirectory();

            lock (_gate)
            {
                if (_store is null || !string.Equals(_rootDirectory, root, StringComparison.OrdinalIgnoreCase))
                {
                    _rootDirectory = root;
                    _store = new SqliteWaitingQuestionStore(root);
                }

                return _store;
            }
        }
    }

    public Task<WaitingQuestion> RegisterAsync(WaitingQuestion candidate, CancellationToken cancellationToken = default) =>
        Current.RegisterAsync(candidate, cancellationToken);

    public Task<WaitingQuestion?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<WaitingQuestion>> ListAsync(bool openOnly = false, CancellationToken cancellationToken = default) =>
        Current.ListAsync(openOnly, cancellationToken);

    public Task<WaitingQuestionOutcome> ChangeAsync(
        Guid id,
        Func<WaitingQuestion, WaitingQuestionOutcome> transition,
        CancellationToken cancellationToken = default) =>
        Current.ChangeAsync(id, transition, cancellationToken);
}
