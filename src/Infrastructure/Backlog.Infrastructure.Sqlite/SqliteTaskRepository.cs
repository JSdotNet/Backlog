using System.Data;
using System.Globalization;
using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.Abstractions;
using Backlog.Modules.Backlog.DomainModels;
using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite;

/// <summary>
/// Local-first <see cref="ITaskRepository"/> that stores every task in one SQLite
/// database file. Fully offline; no cloud dependency.
/// <para>
/// The task's content stays markdown — it is written verbatim into a text column,
/// which is the whole of what "the content is markdown" has to mean once the
/// document is no longer the storage format.
/// </para>
/// <para>
/// Raw ADO.NET rather than an ORM. The port has four members over one table, and
/// an ORM's schema-migration machinery would be a second thing to version for a
/// single local file.
/// </para>
/// </summary>
public sealed class SqliteTaskRepository : ITaskRepository
{
    public const string DatabaseFileName = "backlog.db";

    // The SELECT list, and with it the column ordinals every read below uses.
    private const string Columns =
        "id, title, content_md, type, status, priority, sort_order, area, created_at, " +
        "source_inbox_id, recurrence_source_id, due_on, remind_at, recurrence, in_my_day_on, " +
        "view, tags, repo_ids, depends_on, sub_items, usage_events, projections, effort";

    private readonly string _databasePath;

    /// <summary>Creates a repository over the database in the given folder, or in
    /// the default per-user app-data folder (<c>%LOCALAPPDATA%\Backlog</c>) when
    /// null.</summary>
    public SqliteTaskRepository(string? rootDir = null)
    {
        var root = rootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog");

        _databasePath = DatabasePathFor(root);
    }

    /// <summary>Where the database for a workspace root lives.</summary>
    public static string DatabasePathFor(string rootDir) => Path.Combine(rootDir, DatabaseFileName);

    /// <summary>The database file this repository reads and writes.</summary>
    public string DatabasePath => _databasePath;

    public async Task SaveAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // One statement for create and update alike: the aggregate is written
        // whole either way, so there is nothing for two code paths to disagree
        // about.
        command.CommandText = $"""
            INSERT INTO tasks ({Columns})
            VALUES (
                $id, $title, $content_md, $type, $status, $priority, $sort_order, $area, $created_at,
                $source_inbox_id, $recurrence_source_id, $due_on, $remind_at, $recurrence, $in_my_day_on,
                $view, $tags, $repo_ids, $depends_on, $sub_items, $usage_events, $projections, $effort)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                content_md = excluded.content_md,
                type = excluded.type,
                status = excluded.status,
                priority = excluded.priority,
                sort_order = excluded.sort_order,
                area = excluded.area,
                created_at = excluded.created_at,
                source_inbox_id = excluded.source_inbox_id,
                recurrence_source_id = excluded.recurrence_source_id,
                due_on = excluded.due_on,
                remind_at = excluded.remind_at,
                recurrence = excluded.recurrence,
                in_my_day_on = excluded.in_my_day_on,
                view = excluded.view,
                tags = excluded.tags,
                repo_ids = excluded.repo_ids,
                depends_on = excluded.depends_on,
                sub_items = excluded.sub_items,
                usage_events = excluded.usage_events,
                projections = excluded.projections,
                effort = excluded.effort;
            """;

        command.Parameters.AddWithValue("$id", task.Id.ToString());
        command.Parameters.AddWithValue("$title", task.Title);
        command.Parameters.AddWithValue("$content_md", task.ContentMd);
        command.Parameters.AddWithValue("$type", EnumMap.ToWire(task.Type));
        command.Parameters.AddWithValue("$status", EnumMap.ToWire(task.Status));
        command.Parameters.AddWithValue("$priority", EnumMap.ToWire(task.Priority));
        command.Parameters.AddWithValue("$sort_order", task.Order);
        command.Parameters.AddWithValue("$area", Nullable(task.Area));
        command.Parameters.AddWithValue("$created_at", task.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$source_inbox_id", Nullable(task.SourceInboxId));
        command.Parameters.AddWithValue("$recurrence_source_id", Nullable(task.RecurrenceSourceId?.ToString()));
        command.Parameters.AddWithValue("$due_on", Nullable(WriteDate(task.DueOn)));
        command.Parameters.AddWithValue("$remind_at", Nullable(WriteWallClock(task.RemindAt)));
        command.Parameters.AddWithValue(
            "$recurrence",
            Nullable(task.Recurrence is { } recurrence ? EntryTextParser.RepeatToken(recurrence) : null));
        command.Parameters.AddWithValue("$in_my_day_on", Nullable(WriteDate(task.InMyDayOn)));
        command.Parameters.AddWithValue(
            "$view",
            Nullable(task.View is { } view ? EntryTextParser.ViewToken(view) : null));
        command.Parameters.AddWithValue("$tags", TaskPayloads.Write(task.Tags));
        command.Parameters.AddWithValue("$repo_ids", TaskPayloads.Write(task.RepoIds));
        command.Parameters.AddWithValue("$depends_on", TaskPayloads.Write(task.DependsOn));
        command.Parameters.AddWithValue("$sub_items", TaskPayloads.Write(
            task.SubItems
                .Select(s => new SubItemPayload(s.Id.ToString(), s.Title, EnumMap.ToWire(s.Status), s.Notes, s.Order))
                .ToList()));
        command.Parameters.AddWithValue("$usage_events", TaskPayloads.Write(
            task.UsageEvents.Select(u => new UsageEventPayload(u.Timestamp, u.Action)).ToList()));
        command.Parameters.AddWithValue("$projections", TaskPayloads.Write(
            task.ProjectionRefs
                .Select(p => new ProjectionPayload(p.RepoId, p.ExternalId, p.TargetType))
                .ToList()));
        command.Parameters.AddWithValue("$effort", (object?)task.Effort ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM tasks WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM tasks WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // Hand-ranked order wins; tasks that have never been ranked share the
        // default rank and fall back to newest-first.
        command.CommandText = $"SELECT {Columns} FROM tasks ORDER BY sort_order, created_at DESC;";

        var tasks = new List<TaskItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tasks.Add(Read(reader));
        }

        return tasks;
    }

    // --- Schema -------------------------------------------------------------

    /// <summary>Creates the database and its schema if they are not there yet.
    /// Called on the way into every operation: the statements are idempotent, and
    /// the alternative — caching which paths have been prepared — would be wrong
    /// the first time somebody moved or deleted the file underneath a running
    /// app.</summary>
    private static async Task<SqliteConnection> OpenAsync(string databasePath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;

                CREATE TABLE IF NOT EXISTS tasks (
                    id                   TEXT PRIMARY KEY NOT NULL,
                    title                TEXT NOT NULL,
                    content_md           TEXT NOT NULL DEFAULT '',
                    type                 TEXT NOT NULL,
                    status               TEXT NOT NULL,
                    priority             TEXT NOT NULL,
                    sort_order           INTEGER NOT NULL DEFAULT 0,
                    area                 TEXT NULL,
                    created_at           TEXT NOT NULL,
                    source_inbox_id      TEXT NULL,
                    recurrence_source_id TEXT NULL,
                    due_on               TEXT NULL,
                    remind_at            TEXT NULL,
                    recurrence           TEXT NULL,
                    in_my_day_on         TEXT NULL,
                    view                 TEXT NULL,
                    tags                 TEXT NOT NULL DEFAULT '[]',
                    repo_ids             TEXT NOT NULL DEFAULT '[]',
                    depends_on           TEXT NOT NULL DEFAULT '[]',
                    sub_items            TEXT NOT NULL DEFAULT '[]',
                    usage_events         TEXT NOT NULL DEFAULT '[]',
                    projections          TEXT NOT NULL DEFAULT '[]',
                    effort               INTEGER NULL
                );

                CREATE INDEX IF NOT EXISTS ix_tasks_rank ON tasks (sort_order, created_at DESC);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            // CREATE TABLE IF NOT EXISTS leaves a table that already exists exactly
            // as it was, so a column added to the schema above never reaches a
            // database an earlier build created. This store keeps no migration
            // machinery on purpose (see the class remarks), so an additive column
            // is brought in by hand: ask the table what it has and add only what it
            // is missing. Cheap — one PRAGMA against a single-table file — and
            // idempotent, so it is safe to run on the way into every operation the
            // same way the CREATE above is. Additive-only, which is the only shape
            // of change this local store's one table has ever needed.
            await EnsureColumnAsync(connection, "effort", "INTEGER NULL", cancellationToken).ConfigureAwait(false);

            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Adds a column to the tasks table when it is not already there, and
    /// does nothing when it is. The presence check is a read of
    /// <c>PRAGMA table_info(tasks)</c> rather than a catch around a failing
    /// <c>ALTER</c>, so the ordinary case — the column is present — costs no
    /// exception. <paramref name="column"/> and <paramref name="definition"/> are
    /// compile-time constants from this class and never anything a caller supplies,
    /// which is what makes composing the DDL by string safe here.</summary>
    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        var present = false;
        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = "PRAGMA table_info(tasks);";
            await using var reader = await probe.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var nameOrdinal = reader.GetOrdinal("name");
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(reader.GetString(nameOrdinal), column, StringComparison.OrdinalIgnoreCase))
                {
                    present = true;
                    break;
                }
            }
        }

        if (present) return;

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE tasks ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // --- Reading ------------------------------------------------------------

    /// <summary>Ordinals into the SELECT list above. Named rather than inlined,
    /// because two of the constructor's arguments (repo ids, tags) read in the
    /// opposite order to the columns they come from - and nested so the names
    /// cannot shadow the domain types they are called after.</summary>
    private static class Col
    {
        public const int Id = 0, Title = 1, ContentMd = 2, Type = 3, Status = 4, Priority = 5;
        public const int SortOrder = 6, Area = 7, CreatedAt = 8, SourceInboxId = 9, RecurrenceSourceId = 10;
        public const int DueOn = 11, RemindAt = 12, Recurrence = 13, InMyDayOn = 14, View = 15;
        public const int Tags = 16, RepoIds = 17, DependsOn = 18;
        public const int SubItems = 19, UsageEvents = 20, Projections = 21, Effort = 22;
    }

    private static TaskItem Read(IDataRecord row)
    {
        var task = new TaskItem(
            Guid.Parse(row.GetString(Col.Id)),
            row.GetString(Col.Title),
            row.GetString(Col.ContentMd),
            EnumMap.ParseType(row.GetString(Col.Type)),
            EnumMap.ParseStatus(row.GetString(Col.Status)),
            EnumMap.ParsePriority(row.GetString(Col.Priority)),
            TaskPayloads.Read<string>(Text(row, Col.RepoIds)),
            TaskPayloads.Read<string>(Text(row, Col.Tags)),
            Text(row, Col.SourceInboxId),
            DateTimeOffset.Parse(row.GetString(Col.CreatedAt), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            ParseGuid(Text(row, Col.RecurrenceSourceId)));

        task.SetOrder(row.GetInt32(Col.SortOrder));
        task.SetArea(Text(row, Col.Area));
        task.SetDueOn(ParseDate(Text(row, Col.DueOn)));
        task.SetReminder(ParseWallClock(Text(row, Col.RemindAt)));
        task.SetRecurrence(EntryTextParser.ParseRepeat(Text(row, Col.Recurrence)));
        task.SetInMyDayOn(ParseDate(Text(row, Col.InMyDayOn)));
        task.SetView(EntryTextParser.ParseView(Text(row, Col.View)));
        task.SetDependsOn(TaskPayloads.Read<string>(Text(row, Col.DependsOn)));
        task.SetEffort(Int(row, Col.Effort));

        foreach (var payload in TaskPayloads.Read<SubItemPayload>(Text(row, Col.SubItems)).OrderBy(s => s.Order))
        {
            task.LoadSubItem(task.CreateSubItemForLoad(
                Guid.Parse(payload.Id),
                payload.Title,
                EnumMap.ParseSubItemStatus(payload.Status),
                payload.Notes,
                payload.Order));
        }

        foreach (var payload in TaskPayloads.Read<UsageEventPayload>(Text(row, Col.UsageEvents)))
        {
            task.LoadUsageEvent(new UsageEvent(payload.Timestamp, payload.Action));
        }

        foreach (var payload in TaskPayloads.Read<ProjectionPayload>(Text(row, Col.Projections)))
        {
            task.AddProjectionRef(new ProjectionRef(payload.RepoId, payload.ExternalId, payload.TargetType));
        }

        return task;
    }

    // --- Values -------------------------------------------------------------

    private static string? Text(IDataRecord row, int ordinal) =>
        row.IsDBNull(ordinal) ? null : row.GetString(ordinal);

    /// <summary>Reads a nullable integer column. Null in the database is null here,
    /// which for the effort column is "not estimated" — a different value from a
    /// stored zero, so the read has to keep the two apart rather than fold a
    /// missing value into a default.</summary>
    private static int? Int(IDataRecord row, int ordinal) =>
        row.IsDBNull(ordinal) ? null : row.GetInt32(ordinal);

    private static object Nullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static string? WriteDate(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Writes a reminder without an offset. The domain holds it as
    /// wall-clock intent — 09:00 means 09:00 wherever the person is — so an
    /// offset here would pin it to whichever zone it was written in.</summary>
    private static string? WriteWallClock(DateTime? value) =>
        value?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Reads a stored calendar date. Invariant, because the database may
    /// have been written on another machine and a date is not the place to find
    /// out what culture that machine was set to.</summary>
    private static DateOnly? ParseDate(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    /// <summary>Reads a stored wall-clock date and time. <c>RoundtripKind</c> keeps
    /// the value <see cref="DateTimeKind.Unspecified"/> rather than assuming the
    /// local zone, which is what makes a reminder mean the same clock reading on
    /// the next device to open the database.</summary>
    private static DateTime? ParseWallClock(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var moment)
                ? moment
                : null;

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
