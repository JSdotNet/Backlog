using System.Data;
using System.Globalization;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
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
/// Raw ADO.NET rather than an ORM. The port has three members over one table, and
/// an ORM's schema-migration machinery would be a second thing to version for a
/// single local file.
/// </para>
/// <para>
/// Deleting is a write, not a removal: a task is tombstoned by saving it with its
/// <c>deleted_at</c> set, and both reads here exclude such a row. Nothing purges
/// one yet — the tombstone reaper arrives with the sync service, which is also
/// where the retention it needs gets decided.
/// </para>
/// </summary>
public sealed class SqliteTaskRepository : ITaskRepository
{
    public const string DatabaseFileName = "backlog.db";

    // The SELECT list, and with it the column ordinals every read below uses.
    private const string Columns =
        "id, title, content_md, type, status, priority, sort_order, area, created_at, " +
        "source_inbox_id, recurrence_source_id, due_on, remind_at, recurrence, in_my_day_on, " +
        "view, tags, repo_ids, depends_on, sub_items, usage_events, projections, effort, " +
        "import_plan_id, import_item_id, updated_at, deleted_at";

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
                $view, $tags, $repo_ids, $depends_on, $sub_items, $usage_events, $projections, $effort,
                $import_plan_id, $import_item_id, $updated_at, $deleted_at)
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
                effort = excluded.effort,
                import_plan_id = excluded.import_plan_id,
                import_item_id = excluded.import_item_id,
                updated_at = excluded.updated_at,
                deleted_at = excluded.deleted_at;
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
        command.Parameters.AddWithValue("$import_plan_id", Nullable(task.ImportPlanId));
        command.Parameters.AddWithValue("$import_item_id", Nullable(task.ImportItemId));

        // Same round-trippable format as created_at, and for the same reason: these
        // are instants that have to survive a read on another machine unchanged.
        // The tombstone writes null when the task is live, which is the value rather
        // than a missing one.
        command.Parameters.AddWithValue("$updated_at", task.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$deleted_at",
            Nullable(task.DeletedAt?.ToString("O", CultureInfo.InvariantCulture)));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // A tombstoned task is gone as far as every read is concerned, so the
        // predicate belongs here rather than in each caller: the row survives only
        // far enough for the other devices to learn the task went.
        command.CommandText = $"SELECT {Columns} FROM tasks WHERE id = $id AND deleted_at IS NULL;";
        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // Hand-ranked order wins; tasks that have never been ranked share the
        // default rank and fall back to newest-first. Tombstones are excluded for
        // the same reason GetAsync excludes them.
        command.CommandText =
            $"SELECT {Columns} FROM tasks WHERE deleted_at IS NULL ORDER BY sort_order, created_at DESC;";

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
                    effort               INTEGER NULL,
                    import_plan_id       TEXT NULL,
                    import_item_id       TEXT NULL,
                    -- Nullable even though the domain's UpdatedAt is not: this has
                    -- to match the column ALTER TABLE can add to a database that
                    -- already has rows, and the read coalesces a null to created_at.
                    updated_at           TEXT NULL,
                    deleted_at           TEXT NULL
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
            await EnsureColumnAsync(connection, "import_plan_id", "TEXT NULL", cancellationToken).ConfigureAwait(false);
            await EnsureColumnAsync(connection, "import_item_id", "TEXT NULL", cancellationToken).ConfigureAwait(false);
            await EnsureColumnAsync(connection, "updated_at", "TEXT NULL", cancellationToken).ConfigureAwait(false);
            await EnsureColumnAsync(connection, "deleted_at", "TEXT NULL", cancellationToken).ConfigureAwait(false);

            // And one value the vocabulary retired. `follow_up` was a task type until
            // a follow-up became a relationship between two entries instead of a
            // classification of one, and `EnumMap.ParseType` no longer knows the
            // word — so a row still carrying it would throw on every read, which is
            // somebody's entry lost to a rename. This rewrites the retired value to
            // the type those entries always were.
            //
            // Deliberately not the start of a migration system: there is no version
            // column, no ordered script list, and nothing here reads what ran
            // before. It is the same shape as the additive columns above — a
            // statement that is true after it runs and true again the next time, so
            // it is safe on every open. An UPDATE that matches no row is a scan of a
            // one-table local file and costs nothing, which is what makes running it
            // unconditionally cheaper than remembering whether it has run.
            await NormalizeRetiredTypeAsync(connection, cancellationToken).ConfigureAwait(false);

            // And one value the new columns need seeding with. A row written before
            // updated_at existed has no idea when it last changed, and the only true
            // thing this machine knows about it is when it was created — which is
            // also the weakest correct statement, since a task nobody has edited
            // since really did last change when it was made.
            //
            // Not left null, because null is unusable rather than merely unknown:
            // the push asks for documents changed since a watermark, and
            // `null > watermark` is never true, so a row left null would never
            // travel and would stay invisible to the person's other machine for
            // good. deleted_at gets no equivalent — there, null is the value.
            //
            // Same shape and the same disclaimer as the normalization above: no
            // version column, no ordered script list, nothing reads what ran
            // before, and after it runs no row matches it again. Additive and
            // non-destructive, which is the only kind of change this schema has
            // needed and the boundary at which this approach stops being enough —
            // see .arc42/adr/0006-additive-schema-bootstrapping-is-the-local-migration-mechanism.md.
            await BackfillUpdatedAtAsync(connection, cancellationToken).ConfigureAwait(false);

            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Rewrites the one retired <c>type</c> value to the type it became.
    /// Idempotent by construction: after it runs, no row matches it again. See the
    /// comment at the call site for why this is a normalization rather than a
    /// migration.</summary>
    private static async Task NormalizeRetiredTypeAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE tasks SET type = 'task' WHERE type = 'follow_up';";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gives a row that predates the <c>updated_at</c> column the only
    /// honest value available for it — its own <c>created_at</c>. Idempotent by
    /// construction: after it runs, no row matches it again. See the comment at the
    /// call site for why this is a bootstrap rather than a migration.</summary>
    private static async Task BackfillUpdatedAtAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE tasks SET updated_at = created_at WHERE updated_at IS NULL;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
        public const int ImportPlanId = 23, ImportItemId = 24;
        public const int UpdatedAt = 25, DeletedAt = 26;
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
        task.SetImportPlanId(Text(row, Col.ImportPlanId));
        task.SetImportItemId(Text(row, Col.ImportItemId));

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

        // LAST, and it has to be last. Every setter above is an ordinary
        // command-side mutator that restamps UpdatedAt to now — there is no
        // load-only path for them — so a read that stopped short of here would
        // overwrite the stamp it had just read and make every task look as though
        // it had been edited the instant it was loaded. Moving this call up, or
        // adding another setter after it, silently breaks last-write-wins;
        // SqliteTaskRepositoryTests pins the round trip.
        //
        // updated_at is coalesced to created_at rather than trusted to be present:
        // the column arrived after rows already existed, and a row inserted by
        // something that does not know about it still has to read.
        task.LoadStamps(
            ParseInstant(Text(row, Col.UpdatedAt)) ?? task.CreatedAt,
            ParseInstant(Text(row, Col.DeletedAt)));

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

    /// <summary>Reads a stored instant — the sync stamps, written with the same
    /// round-trippable format as <c>created_at</c>. <c>RoundtripKind</c> keeps the
    /// offset that was written instead of reinterpreting it in the reader's zone,
    /// which is what lets two machines compare the same value and agree.
    /// <para>
    /// Null for an absent or unreadable value rather than throwing. Absent is
    /// ordinary here: <c>deleted_at</c> is null for every live task, and
    /// <c>updated_at</c> is null on any row written before the column existed.
    /// </para></summary>
    private static DateTimeOffset? ParseInstant(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var instant)
                ? instant
                : null;

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
