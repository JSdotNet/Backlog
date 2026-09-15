using System.Data;
using System.Globalization;

using Backlog.Modules.Inbox;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Inbox;

/// <summary>
/// Local-first store for the Inbox module — its items, lists and groups — in
/// three tables of the same <c>backlog.db</c> the tasks and the roadmap plan
/// live in. Fully offline; no cloud dependency.
/// <para>
/// One class answering both <see cref="IInboxItemRepository"/> and
/// <see cref="IInboxOrganizerRepository"/>, because the two ports are one
/// module's persistence over one file and splitting them would be two copies
/// of the same <c>OpenAsync</c>. The ports stay separate on the module side,
/// where the separation means something.
/// </para>
/// <para>
/// This repository creates <em>only</em> its own three tables, and never reads
/// <c>tasks</c> or <c>roadmap_plan</c>. Inherited ADR 0014 puts persistence in
/// the hands of the module that owns the data; what the adapters here share is
/// a file, not a schema. The tables are created by idempotent
/// <c>IF NOT EXISTS</c> DDL on every open (local ADR 0003); a column added
/// later goes through <see cref="EnsureColumnAsync"/> (local ADR 0006). None is
/// needed at birth — the method exists so the next one has a home.
/// </para>
/// <para>
/// Lists and groups are hard-deleted. Tombstoning is for documents that travel
/// (local ADR 0005) and nothing replicates the organiser. Items are never
/// deleted at all in this scope; archived is their terminal state.
/// </para>
/// </summary>
public sealed class SqliteInboxRepository : IInboxItemRepository, IInboxOrganizerRepository
{
    // The SELECT lists, and with them the column ordinals every read below uses.
    private const string ItemColumns =
        "id, title, body_md, source_url, captured_at, received_at, status, deferred_until, kind, " +
        "channel, person, tags, repo_ids, list_id, routing_domain, routing_repo_ids, routing_task_ids, " +
        "routed_at, replica_backed, replica_ack_pending, updated_at";

    private const string ListColumns = "id, name, group_id, sort_order, created_at, updated_at";

    private const string GroupColumns = "id, name, sort_order, created_at, updated_at";

    private readonly string _databasePath;

    /// <summary>Creates a repository over the database in the given folder, or
    /// in the default per-user app-data folder when null. The same root the
    /// tasks use, and the same file inside it.</summary>
    public SqliteInboxRepository(string? rootDir = null)
    {
        var root = rootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog");

        _databasePath = SqliteTaskRepository.DatabasePathFor(root);
    }

    /// <summary>The database file this repository reads and writes — the same
    /// one <see cref="SqliteTaskRepository"/> uses.</summary>
    public string DatabasePath => _databasePath;

    // --- Items --------------------------------------------------------------

    public async Task SaveAsync(InboxItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // One statement for create and update alike: the aggregate is written
        // whole either way, so there is nothing for two code paths to disagree
        // about.
        command.CommandText = $"""
            INSERT INTO inbox_items ({ItemColumns})
            VALUES (
                $id, $title, $body_md, $source_url, $captured_at, $received_at, $status, $deferred_until, $kind,
                $channel, $person, $tags, $repo_ids, $list_id, $routing_domain, $routing_repo_ids, $routing_task_ids,
                $routed_at, $replica_backed, $replica_ack_pending, $updated_at)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                body_md = excluded.body_md,
                source_url = excluded.source_url,
                captured_at = excluded.captured_at,
                received_at = excluded.received_at,
                status = excluded.status,
                deferred_until = excluded.deferred_until,
                kind = excluded.kind,
                channel = excluded.channel,
                person = excluded.person,
                tags = excluded.tags,
                repo_ids = excluded.repo_ids,
                list_id = excluded.list_id,
                routing_domain = excluded.routing_domain,
                routing_repo_ids = excluded.routing_repo_ids,
                routing_task_ids = excluded.routing_task_ids,
                routed_at = excluded.routed_at,
                replica_backed = excluded.replica_backed,
                replica_ack_pending = excluded.replica_ack_pending,
                updated_at = excluded.updated_at;
            """;

        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$body_md", item.BodyMd);
        command.Parameters.AddWithValue("$source_url", Nullable(item.SourceUrl));
        command.Parameters.AddWithValue("$captured_at", WriteInstant(item.CapturedAt));
        command.Parameters.AddWithValue("$received_at", WriteInstant(item.ReceivedAt));
        command.Parameters.AddWithValue("$status", InboxEnumMap.ToWire(item.Status));
        command.Parameters.AddWithValue("$deferred_until", Nullable(WriteDate(item.DeferredUntil)));
        // The raw slug rather than ToWire(Kind), so a kind this build could not
        // read goes back exactly as it came.
        command.Parameters.AddWithValue("$kind", item.KindSlug);
        command.Parameters.AddWithValue("$channel", item.Source.Channel);
        command.Parameters.AddWithValue("$person", Nullable(item.Source.Person));
        command.Parameters.AddWithValue("$tags", TaskPayloads.Write(
            item.Tags.Select(tag => new InboxTagPayload(tag.Name, tag.AutoGenerated)).ToList()));
        command.Parameters.AddWithValue("$repo_ids", TaskPayloads.Write(item.RepoIds));
        command.Parameters.AddWithValue("$list_id", Nullable(item.ListId?.ToString()));
        command.Parameters.AddWithValue("$routing_domain", Nullable(item.Routing is { } routing ? InboxEnumMap.ToWire(routing.Domain) : null));
        command.Parameters.AddWithValue("$routing_repo_ids", TaskPayloads.Write(item.Routing?.RepoIds ?? []));
        command.Parameters.AddWithValue("$routing_task_ids", TaskPayloads.Write(
            (item.Routing?.TaskIds ?? []).Select(id => id.ToString()).ToList()));
        command.Parameters.AddWithValue("$routed_at", Nullable(item.Routing is { } routed ? WriteInstant(routed.RoutedAt) : null));
        command.Parameters.AddWithValue("$replica_backed", item.ReplicaBacked ? 1 : 0);
        command.Parameters.AddWithValue("$replica_ack_pending", item.ReplicaAckPending ? 1 : 0);
        command.Parameters.AddWithValue("$updated_at", WriteInstant(item.UpdatedAt));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<InboxItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {ItemColumns} FROM inbox_items WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadItem(reader) : null;
    }

    public Task<IReadOnlyList<InboxItem>> ListAsync(CancellationToken cancellationToken = default) =>
        // Newest capture first, every status: the pane slices by status itself.
        ReadItemsAsync($"SELECT {ItemColumns} FROM inbox_items ORDER BY captured_at DESC;", cancellationToken);

    public Task<IReadOnlyList<InboxItem>> ListPendingReplicaAckAsync(CancellationToken cancellationToken = default) =>
        ReadItemsAsync(
            $"SELECT {ItemColumns} FROM inbox_items WHERE replica_ack_pending = 1 ORDER BY updated_at;",
            cancellationToken);

    private async Task<IReadOnlyList<InboxItem>> ReadItemsAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var items = new List<InboxItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(ReadItem(reader));
        }

        return items;
    }

    // --- Lists and groups ---------------------------------------------------

    public async Task<IReadOnlyList<InboxList>> ListListsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {ListColumns} FROM inbox_lists ORDER BY sort_order, name COLLATE NOCASE;";

        var lists = new List<InboxList>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            lists.Add(ReadList(reader));
        }

        return lists;
    }

    public async Task<IReadOnlyList<InboxGroup>> ListGroupsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {GroupColumns} FROM inbox_groups ORDER BY sort_order, name COLLATE NOCASE;";

        var groups = new List<InboxGroup>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            groups.Add(ReadGroup(reader));
        }

        return groups;
    }

    public async Task SaveListAsync(InboxList list, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO inbox_lists ({ListColumns})
            VALUES ($id, $name, $group_id, $sort_order, $created_at, $updated_at)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                group_id = excluded.group_id,
                sort_order = excluded.sort_order,
                created_at = excluded.created_at,
                updated_at = excluded.updated_at;
            """;

        command.Parameters.AddWithValue("$id", list.Id.ToString());
        command.Parameters.AddWithValue("$name", list.Name);
        command.Parameters.AddWithValue("$group_id", Nullable(list.GroupId?.ToString()));
        command.Parameters.AddWithValue("$sort_order", list.Order);
        command.Parameters.AddWithValue("$created_at", WriteInstant(list.CreatedAt));
        command.Parameters.AddWithValue("$updated_at", WriteInstant(list.UpdatedAt));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteListAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM inbox_lists WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveGroupAsync(InboxGroup group, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO inbox_groups ({GroupColumns})
            VALUES ($id, $name, $sort_order, $created_at, $updated_at)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                sort_order = excluded.sort_order,
                created_at = excluded.created_at,
                updated_at = excluded.updated_at;
            """;

        command.Parameters.AddWithValue("$id", group.Id.ToString());
        command.Parameters.AddWithValue("$name", group.Name);
        command.Parameters.AddWithValue("$sort_order", group.Order);
        command.Parameters.AddWithValue("$created_at", WriteInstant(group.CreatedAt));
        command.Parameters.AddWithValue("$updated_at", WriteInstant(group.UpdatedAt));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM inbox_groups WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // --- Schema -------------------------------------------------------------

    /// <summary>Creates the database and the three inbox tables if they are not
    /// there yet. Called on the way into every operation, for the reason
    /// <see cref="SqliteTaskRepository"/> gives: the statements are idempotent,
    /// and caching which paths have been prepared would be wrong the first time
    /// somebody moved or deleted the file underneath a running app.</summary>
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

                CREATE TABLE IF NOT EXISTS inbox_groups (
                    id          TEXT PRIMARY KEY NOT NULL,
                    name        TEXT NOT NULL,
                    sort_order  INTEGER NOT NULL DEFAULT 0,
                    created_at  TEXT NOT NULL,
                    updated_at  TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS inbox_lists (
                    id          TEXT PRIMARY KEY NOT NULL,
                    name        TEXT NOT NULL,
                    -- No foreign key on purpose: SQLite leaves FK enforcement off
                    -- by default, and ungrouping is a handler write rather than a
                    -- cascade the store may or may not run.
                    group_id    TEXT NULL,
                    sort_order  INTEGER NOT NULL DEFAULT 0,
                    created_at  TEXT NOT NULL,
                    updated_at  TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS inbox_items (
                    -- The replica capture id when replica_backed = 1, so intake is
                    -- idempotent by primary key.
                    id                   TEXT PRIMARY KEY NOT NULL,
                    title                TEXT NOT NULL,
                    body_md              TEXT NOT NULL DEFAULT '',
                    source_url           TEXT NULL,
                    captured_at          TEXT NOT NULL,
                    received_at          TEXT NOT NULL,
                    status               TEXT NOT NULL,
                    deferred_until       TEXT NULL,
                    -- A CaptureKinds slug; one this build does not know is kept
                    -- verbatim rather than rewritten to the nearest member.
                    kind                 TEXT NOT NULL,
                    channel              TEXT NOT NULL,
                    person               TEXT NULL,
                    tags                 TEXT NOT NULL DEFAULT '[]',
                    repo_ids             TEXT NOT NULL DEFAULT '[]',
                    list_id              TEXT NULL,
                    routing_domain       TEXT NULL,
                    routing_repo_ids     TEXT NOT NULL DEFAULT '[]',
                    routing_task_ids     TEXT NOT NULL DEFAULT '[]',
                    routed_at            TEXT NULL,
                    replica_backed       INTEGER NOT NULL DEFAULT 0,
                    replica_ack_pending  INTEGER NOT NULL DEFAULT 0,
                    -- NOT NULL, unlike the tasks table's updated_at: that column
                    -- was added to a populated table and had to tolerate null.
                    -- These three ship with their tables, so no row has ever
                    -- lacked one (local ADR 0006's asymmetry note).
                    updated_at           TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_inbox_items_captured ON inbox_items (captured_at DESC);
                CREATE INDEX IF NOT EXISTS ix_inbox_items_list     ON inbox_items (list_id);
                CREATE INDEX IF NOT EXISTS ix_inbox_items_ack      ON inbox_items (replica_ack_pending);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            // No EnsureColumnAsync calls yet: every column above shipped with its
            // table. The method is here so the first additive column has the same
            // home it has in SqliteTaskRepository, and the same rules.

            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Adds a column to one of the inbox tables when it is not already
    /// there, and does nothing when it is — the same additive-only bootstrap
    /// <see cref="SqliteTaskRepository"/> keeps (local ADR 0006). Unused until the
    /// first column is added after a table has rows, and kept so that column has a
    /// home rather than a fresh idea. <paramref name="table"/>,
    /// <paramref name="column"/> and <paramref name="definition"/> are compile-time
    /// constants from this class and never anything a caller supplies, which is
    /// what makes composing the DDL by string safe here.</summary>
    internal static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string table,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        var present = false;
        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = $"PRAGMA table_info({table});";
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
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // --- Reading ------------------------------------------------------------

    /// <summary>Ordinals into the SELECT lists above. Named rather than inlined,
    /// and nested so the names cannot shadow the domain types they are called
    /// after.</summary>
    private static class Col
    {
        public const int Id = 0, Title = 1, BodyMd = 2, SourceUrl = 3, CapturedAt = 4, ReceivedAt = 5;
        public const int Status = 6, DeferredUntil = 7, Kind = 8, Channel = 9, Person = 10;
        public const int Tags = 11, RepoIds = 12, ListId = 13;
        public const int RoutingDomain = 14, RoutingRepoIds = 15, RoutingTaskIds = 16, RoutedAt = 17;
        public const int ReplicaBacked = 18, ReplicaAckPending = 19, UpdatedAt = 20;

        public const int ListName = 1, ListGroupId = 2, ListOrder = 3, ListCreatedAt = 4, ListUpdatedAt = 5;
        public const int GroupName = 1, GroupOrder = 2, GroupCreatedAt = 3, GroupUpdatedAt = 4;
    }

    private static InboxItem ReadItem(IDataRecord row)
    {
        var slug = row.GetString(Col.Kind);

        var item = new InboxItem(
            Guid.Parse(row.GetString(Col.Id)),
            row.GetString(Col.Title),
            row.GetString(Col.BodyMd),
            Text(row, Col.SourceUrl),
            ParseInstant(row.GetString(Col.CapturedAt)),
            ParseInstant(row.GetString(Col.ReceivedAt)),
            InboxEnumMap.ParseKind(slug),
            new InboxSource(row.GetString(Col.Channel), Text(row, Col.Person)),
            row.GetInt32(Col.ReplicaBacked) != 0);

        // The slug as stored, which for an unknown kind differs from what the
        // constructor derived from the parsed member.
        item.SetKind(item.Kind, slug);
        // Loaded, not set: SetTags applies the rules a person's edit has to
        // pass, and a row written before a rule existed would fail it on read
        // and take every row in the list down with it.
        item.LoadTags(TaskPayloads.Read<InboxTagPayload>(Text(row, Col.Tags))
            .Select(tag => new InboxTag(tag.Name, tag.Auto)));
        item.SetRepoIds(TaskPayloads.Read<string>(Text(row, Col.RepoIds)));
        item.MoveToList(ParseGuid(Text(row, Col.ListId)));

        RoutingTarget? routing = null;
        if (Text(row, Col.RoutingDomain) is { } domain && Text(row, Col.RoutedAt) is { } routedAt)
        {
            routing = new RoutingTarget(
                InboxEnumMap.ParseRoutingDomain(domain),
                TaskPayloads.Read<string>(Text(row, Col.RoutingRepoIds)),
                [.. TaskPayloads.Read<string>(Text(row, Col.RoutingTaskIds)).Select(Guid.Parse)],
                ParseInstant(routedAt));
        }

        // LAST, and it has to be last: every setter above restamps UpdatedAt to
        // now, and the lifecycle fields go through here because the guarded
        // mutators refuse the transitions a stored row has already made.
        item.LoadState(
            InboxEnumMap.ParseStatus(row.GetString(Col.Status)),
            ParseDate(Text(row, Col.DeferredUntil)),
            routing,
            row.GetInt32(Col.ReplicaAckPending) != 0,
            ParseInstant(row.GetString(Col.UpdatedAt)));

        return item;
    }

    private static InboxList ReadList(IDataRecord row)
    {
        var list = new InboxList(
            Guid.Parse(row.GetString(Col.Id)),
            row.GetString(Col.ListName),
            ParseGuid(Text(row, Col.ListGroupId)),
            row.GetInt32(Col.ListOrder),
            ParseInstant(row.GetString(Col.ListCreatedAt)));

        list.LoadStamps(ParseInstant(row.GetString(Col.ListUpdatedAt)));

        return list;
    }

    private static InboxGroup ReadGroup(IDataRecord row)
    {
        var group = new InboxGroup(
            Guid.Parse(row.GetString(Col.Id)),
            row.GetString(Col.GroupName),
            row.GetInt32(Col.GroupOrder),
            ParseInstant(row.GetString(Col.GroupCreatedAt)));

        group.LoadStamps(ParseInstant(row.GetString(Col.GroupUpdatedAt)));

        return group;
    }

    // --- Values -------------------------------------------------------------

    private static string? Text(IDataRecord row, int ordinal) =>
        row.IsDBNull(ordinal) ? null : row.GetString(ordinal);

    private static object Nullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    /// <summary>Round-trippable and UTC, the same format every stamp in this
    /// file uses, so lexical order in SQLite is chronological order.</summary>
    private static string WriteInstant(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseInstant(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string? WriteDate(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Invariant, because the database may have been written on
    /// another machine and a date is not the place to find out what culture that
    /// machine was set to.</summary>
    private static DateOnly? ParseDate(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
