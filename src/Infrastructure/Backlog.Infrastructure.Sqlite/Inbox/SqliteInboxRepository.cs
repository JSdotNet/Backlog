using System.Data;
using System.Globalization;

using Backlog.Modules.Inbox;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Inbox;

/// <summary>
/// Local-first store for the Inbox module — its items and their files, lists and
/// groups — in four tables of the same <c>backlog.db</c> the tasks and the
/// roadmap plan live in. Fully offline; no cloud dependency.
/// <para>
/// One class answering both <see cref="IInboxItemRepository"/> and
/// <see cref="IInboxOrganizerRepository"/>, because the two ports are one
/// module's persistence over one file and splitting them would be two copies
/// of the same <c>OpenAsync</c>. The ports stay separate on the module side,
/// where the separation means something.
/// </para>
/// <para>
/// This repository creates <em>only</em> its own tables, and never reads
/// <c>tasks</c> or <c>roadmap_plan</c>. Inherited ADR 0014 puts persistence in
/// the hands of the module that owns the data; what the adapters here share is
/// a file, not a schema. The tables are created by idempotent
/// <c>IF NOT EXISTS</c> DDL on every open (local ADR 0003); a column added
/// later goes through <see cref="EnsureColumnAsync"/> (local ADR 0006) — the
/// first was <c>dismissed_suggestions</c>, the suggestions a reader turned down,
/// and the second <c>duplicate_of</c>, the item one was archived as a duplicate of.
/// </para>
/// <para>
/// Lists and groups are hard-deleted. Tombstoning is for documents that travel
/// (local ADR 0005) and nothing replicates the organiser. So are items: a
/// deleted item's row and files go, and all that stays — in
/// <c>inbox_deleted_captures</c>, and only for an item from the replica — is
/// the acknowledgement the phone is still owed.
/// </para>
/// </summary>
public sealed class SqliteInboxRepository : IInboxItemRepository, IInboxOrganizerRepository
{
    // The SELECT lists, and with them the column ordinals every read below uses.
    private const string ItemColumns =
        "id, title, body_md, source_url, captured_at, received_at, status, deferred_until, kind, " +
        "channel, person, tags, repo_ids, list_id, routing_domain, routing_repo_ids, routing_task_ids, " +
        "routed_at, replica_backed, replica_ack_pending, updated_at, dismissed_suggestions, duplicate_of";

    private const string AttachmentColumns =
        "item_id, attachment_id, name, content_type, size_bytes, sha256, local_path, downloaded_at, last_error, sort_order";

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

        // One transaction for the row and its files, so a reader never sees an
        // item with half its attachments — the rows are replaced whole below.
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        // One statement for create and update alike: the aggregate is written
        // whole either way, so there is nothing for two code paths to disagree
        // about.
        command.CommandText = $"""
            INSERT INTO inbox_items ({ItemColumns})
            VALUES (
                $id, $title, $body_md, $source_url, $captured_at, $received_at, $status, $deferred_until, $kind,
                $channel, $person, $tags, $repo_ids, $list_id, $routing_domain, $routing_repo_ids, $routing_task_ids,
                $routed_at, $replica_backed, $replica_ack_pending, $updated_at, $dismissed_suggestions, $duplicate_of)
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
                updated_at = excluded.updated_at,
                dismissed_suggestions = excluded.dismissed_suggestions,
                duplicate_of = excluded.duplicate_of;
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
        command.Parameters.AddWithValue("$dismissed_suggestions", TaskPayloads.Write(item.DismissedSuggestions));
        command.Parameters.AddWithValue("$duplicate_of", Nullable(item.DuplicateOf?.ToString()));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await SaveAttachmentsAsync(connection, transaction, item, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces the item's attachment rows with the aggregate's list —
    /// the same whole-aggregate write the item row gets, over a child table
    /// because a list of files is not a JSON column anything should have to
    /// parse to answer "which items still wait for a file".</summary>
    private static async Task SaveAttachmentsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        InboxItem item,
        CancellationToken cancellationToken)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM inbox_item_attachments WHERE item_id = $item_id;";
            delete.Parameters.AddWithValue("$item_id", item.Id.ToString());
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        for (var order = 0; order < item.Attachments.Count; order++)
        {
            var attachment = item.Attachments[order];

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"""
                INSERT INTO inbox_item_attachments ({AttachmentColumns})
                VALUES ($item_id, $attachment_id, $name, $content_type, $size_bytes, $sha256,
                        $local_path, $downloaded_at, $last_error, $sort_order);
                """;

            insert.Parameters.AddWithValue("$item_id", item.Id.ToString());
            insert.Parameters.AddWithValue("$attachment_id", attachment.Id.ToString());
            insert.Parameters.AddWithValue("$name", attachment.Name);
            insert.Parameters.AddWithValue("$content_type", attachment.ContentType);
            insert.Parameters.AddWithValue("$size_bytes", attachment.SizeBytes);
            insert.Parameters.AddWithValue("$sha256", attachment.Sha256);
            insert.Parameters.AddWithValue("$local_path", Nullable(attachment.LocalPath));
            insert.Parameters.AddWithValue("$downloaded_at", Nullable(attachment.DownloadedAt is { } at ? WriteInstant(at) : null));
            insert.Parameters.AddWithValue("$last_error", Nullable(attachment.LastError));
            insert.Parameters.AddWithValue("$sort_order", order);

            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<InboxItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);

        InboxItem? item;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT {ItemColumns} FROM inbox_items WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString());

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            item = await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadItem(reader) : null;
        }

        if (item is not null)
        {
            var attachments = await ReadAttachmentsAsync(connection, item.Id, cancellationToken).ConfigureAwait(false);
            if (attachments.TryGetValue(item.Id, out var files)) item.LoadAttachments(files);
        }

        return item;
    }

    /// <summary>Every attachment row, or one item's, grouped by item and in the
    /// order they were recorded. One read for a whole list, rather than one per
    /// item: the inbox is read whole on every reload.</summary>
    private static async Task<Dictionary<Guid, List<InboxAttachment>>> ReadAttachmentsAsync(
        SqliteConnection connection,
        Guid? itemId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = itemId is null
            ? $"SELECT {AttachmentColumns} FROM inbox_item_attachments ORDER BY item_id, sort_order;"
            : $"SELECT {AttachmentColumns} FROM inbox_item_attachments WHERE item_id = $item_id ORDER BY sort_order;";
        if (itemId is { } id) command.Parameters.AddWithValue("$item_id", id.ToString());

        var byItem = new Dictionary<Guid, List<InboxAttachment>>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var owner = Guid.Parse(reader.GetString(AttachmentCol.ItemId));
            if (!byItem.TryGetValue(owner, out var list)) byItem[owner] = list = [];

            list.Add(InboxAttachment.Load(
                Guid.Parse(reader.GetString(AttachmentCol.AttachmentId)),
                reader.GetString(AttachmentCol.Name),
                reader.GetString(AttachmentCol.ContentType),
                reader.GetInt64(AttachmentCol.SizeBytes),
                reader.GetString(AttachmentCol.Sha256),
                Text(reader, AttachmentCol.LocalPath),
                Text(reader, AttachmentCol.DownloadedAt) is { } at ? ParseInstant(at) : null,
                Text(reader, AttachmentCol.LastError)));
        }

        return byItem;
    }

    public Task<IReadOnlyList<InboxItem>> ListAsync(CancellationToken cancellationToken = default) =>
        // Newest capture first, every status: the pane slices by status itself.
        ReadItemsAsync($"SELECT {ItemColumns} FROM inbox_items ORDER BY captured_at DESC;", cancellationToken);

    public Task<IReadOnlyList<InboxItem>> ListPendingReplicaAckAsync(CancellationToken cancellationToken = default) =>
        ReadItemsAsync(
            $"SELECT {ItemColumns} FROM inbox_items WHERE replica_ack_pending = 1 ORDER BY updated_at;",
            cancellationToken);

    public async Task DeleteAsync(InboxItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.Deleted) throw new ArgumentException("Only an item Delete() has run on is removed.", nameof(item));

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);

        // One transaction, so the item never goes without its acknowledgement
        // staying behind when it owes one.
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM inbox_item_attachments WHERE item_id = $id;
            DELETE FROM inbox_items WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (item.ReplicaAckPending)
        {
            await using var keep = connection.CreateCommand();
            keep.Transaction = transaction;
            keep.CommandText = """
                INSERT INTO inbox_deleted_captures (id, title, captured_at, deleted_at)
                VALUES ($id, $title, $captured_at, $deleted_at)
                ON CONFLICT(id) DO UPDATE SET deleted_at = excluded.deleted_at;
                """;
            keep.Parameters.AddWithValue("$id", item.Id.ToString());
            keep.Parameters.AddWithValue("$title", item.Title);
            keep.Parameters.AddWithValue("$captured_at", WriteInstant(item.CapturedAt));
            keep.Parameters.AddWithValue("$deleted_at", WriteInstant(item.UpdatedAt));
            await keep.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<InboxDeletedCapture>> ListDeletedCapturesAsync(CancellationToken cancellationToken = default) =>
        await ReadDeletedCapturesAsync(
            "SELECT id, title, captured_at, deleted_at FROM inbox_deleted_captures ORDER BY deleted_at;",
            id: null,
            cancellationToken).ConfigureAwait(false);

    public async Task<InboxDeletedCapture?> GetDeletedCaptureAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await ReadDeletedCapturesAsync(
            "SELECT id, title, captured_at, deleted_at FROM inbox_deleted_captures WHERE id = $id;",
            id,
            cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    public async Task ForgetDeletedCaptureAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM inbox_deleted_captures WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<InboxDeletedCapture>> ReadDeletedCapturesAsync(
        string sql,
        Guid? id,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (id is { } value) command.Parameters.AddWithValue("$id", value.ToString());

        var captures = new List<InboxDeletedCapture>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            captures.Add(new InboxDeletedCapture(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                ParseInstant(reader.GetString(2)),
                ParseInstant(reader.GetString(3))));
        }

        return captures;
    }

    private async Task<IReadOnlyList<InboxItem>> ReadItemsAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var items = new List<InboxItem>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(ReadItem(reader));
            }
        }

        var attachments = await ReadAttachmentsAsync(connection, itemId: null, cancellationToken).ConfigureAwait(false);
        foreach (var item in items)
        {
            if (attachments.TryGetValue(item.Id, out var files)) item.LoadAttachments(files);
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

    /// <summary>Creates the database and the inbox tables if they are not
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

                -- The files a capture arrived with (local ADR 0014), one row each.
                -- A table of its own added beside the others rather than a column
                -- on inbox_items: additive, so a database written before it
                -- simply has none (local ADR 0006). The first five columns are
                -- what the capture said and never change; the last three are
                -- this machine's copy.
                CREATE TABLE IF NOT EXISTS inbox_item_attachments (
                    item_id        TEXT NOT NULL,
                    attachment_id  TEXT NOT NULL,
                    name           TEXT NOT NULL,
                    content_type   TEXT NOT NULL,
                    size_bytes     INTEGER NOT NULL,
                    sha256         TEXT NOT NULL,
                    local_path     TEXT NULL,
                    downloaded_at  TEXT NULL,
                    last_error     TEXT NULL,
                    sort_order     INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (item_id, attachment_id)
                );

                -- What is left of a deleted replica-backed item until the phone
                -- has its tombstone (local ADR 0009): enough to rebuild the
                -- capture document, and nothing else. A table of its own, so the
                -- item row really is gone, and additive (local ADR 0006).
                CREATE TABLE IF NOT EXISTS inbox_deleted_captures (
                    id           TEXT PRIMARY KEY NOT NULL,
                    title        TEXT NOT NULL,
                    captured_at  TEXT NOT NULL,
                    deleted_at   TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            // Columns added after inbox_items had rows. Every column above shipped
            // with its table; these arrive on a database written before them, so
            // each carries a default a row written then can stand on.
            await EnsureColumnAsync(connection, "inbox_items", "dismissed_suggestions", "TEXT NOT NULL DEFAULT '[]'", cancellationToken)
                .ConfigureAwait(false);
            await EnsureColumnAsync(connection, "inbox_items", "duplicate_of", "TEXT NULL", cancellationToken)
                .ConfigureAwait(false);

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
    /// <see cref="SqliteTaskRepository"/> keeps (local ADR 0006). <paramref name="table"/>,
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
        public const int ReplicaBacked = 18, ReplicaAckPending = 19, UpdatedAt = 20, DismissedSuggestions = 21;
        public const int DuplicateOf = 22;

        public const int ListName = 1, ListGroupId = 2, ListOrder = 3, ListCreatedAt = 4, ListUpdatedAt = 5;
        public const int GroupName = 1, GroupOrder = 2, GroupCreatedAt = 3, GroupUpdatedAt = 4;
    }

    /// <summary>Ordinals into <see cref="AttachmentColumns"/>.</summary>
    private static class AttachmentCol
    {
        public const int ItemId = 0, AttachmentId = 1, Name = 2, ContentType = 3, SizeBytes = 4, Sha256 = 5;
        public const int LocalPath = 6, DownloadedAt = 7, LastError = 8;
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
        item.LoadDismissedSuggestions(TaskPayloads.Read<string>(Text(row, Col.DismissedSuggestions)));

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
            ParseInstant(row.GetString(Col.UpdatedAt)),
            ParseGuid(Text(row, Col.DuplicateOf)));

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
