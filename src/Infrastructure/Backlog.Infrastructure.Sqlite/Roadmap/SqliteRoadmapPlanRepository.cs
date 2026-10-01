using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Roadmap;

/// <summary>
/// Local-first <see cref="IRoadmapPlanRepository"/> that keeps the whole plan in one
/// row of the same SQLite database the tasks live in. Fully offline; no cloud
/// dependency.
/// <para>
/// The plan used to be <c>_roadmap/plan.json</c> under the storage root. It moved
/// here for the reason local ADR 0005 gives for the tasks: a single file under a
/// root somebody may have pointed at a synced folder is a file a sync product will
/// happily conflict, and one database is one thing to back up and one protocol to
/// keep correct rather than two.
/// </para>
/// <para>
/// One table, one row. The plan is one consistency boundary — a dependency edge is
/// only valid with respect to every other node — so there is no half of it worth
/// storing on its own, and a row per item would buy indexing for a shape nothing
/// ever queries into. That is the same argument <c>TaskPayloads</c> makes for the
/// task's owned collections, one level up.
/// </para>
/// <para>
/// The write no longer has to be made atomic by hand. The file version serialized to
/// a temporary file and moved it over the previous one, because a torn write would
/// have cost the whole plan rather than one entry; a single UPSERT is atomic
/// already, and is strictly stronger than the move ever was.
/// </para>
/// <para>
/// What the move costs, stated plainly: the plan is no longer a file anybody can open
/// in an editor. The byte-order-mark rule and the hand-editability the JSON file was
/// shaped around went with it. The stored document keeps its shape, so a plan pulled
/// out with <c>sqlite3</c> still reads as the same JSON — but editing it is now a job
/// for the app.
/// </para>
/// <para>
/// <b>The row is also the document that travels.</b> Local ADR 0018 replicates the
/// plan between a person's devices as one whole document, and this row already is
/// that document: <see cref="ReadAsync"/> hands out the stored text and its stamp,
/// and <see cref="TryWriteAsync"/> takes another device's copy verbatim at the stamp
/// it arrived with. Verbatim rather than through the aggregate, so a field a newer
/// build wrote survives on disk here even though this build cannot read it.
/// </para>
/// </summary>
public sealed class SqliteRoadmapPlanRepository : IRoadmapPlanRepository, IRoadmapReplicaStore
{
    /// <summary>The row's identity. One workspace plans one roadmap, so the id is a
    /// constant rather than something to look up — it exists to give the UPSERT below
    /// a conflict target, the same way a task's id does.</summary>
    public const string PlanRowId = "plan";

    private readonly string _databasePath;

    /// <summary>Creates a repository over the database in the given folder, or in the
    /// default per-user app-data folder (<c>%LOCALAPPDATA%\Backlog</c>) when null. The
    /// same root the tasks use, and the same file inside it.</summary>
    public SqliteRoadmapPlanRepository(string? rootDir = null)
    {
        var root = rootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog");

        _databasePath = SqliteTaskRepository.DatabasePathFor(root);
    }

    /// <summary>The database file this repository reads and writes — the same one
    /// <see cref="SqliteTaskRepository"/> uses.</summary>
    public string DatabasePath => _databasePath;

    public async Task<RoadmapPlan> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT document FROM roadmap_plan WHERE id = $id;";
        command.Parameters.AddWithValue("$id", PlanRowId);

        var document = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (string.IsNullOrWhiteSpace(document)) return RoadmapPlan.Empty();

        try
        {
            return JsonSerializer.Deserialize<RoadmapPlanDocument>(document, RoadmapPlanDocument.JsonOptions)
                ?.ToPlan()
                ?? RoadmapPlan.Empty();
        }
        catch (JsonException)
        {
            // A stored document that will not parse must never stop the app from
            // opening, and it is deliberately not rewritten or deleted either: an
            // empty plan is shown and the row stays exactly as it is. Overwriting here
            // would turn a bad write into data loss — the same trade the file version
            // made, and the reason it survives the move.
            return RoadmapPlan.Empty();
        }
    }

    public async Task SaveAsync(RoadmapPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var document = JsonSerializer.Serialize(
            RoadmapPlanDocument.From(plan), RoadmapPlanDocument.JsonOptions);

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // One statement for the first save and every later one alike: the plan is
        // written whole either way, so there is nothing for two code paths to
        // disagree about.
        command.CommandText =
            """
            INSERT INTO roadmap_plan (id, document, updated_at)
            VALUES ($id, $document, $updated_at)
            ON CONFLICT(id) DO UPDATE SET
                document = excluded.document,
                updated_at = excluded.updated_at;
            """;

        command.Parameters.AddWithValue("$id", PlanRowId);
        command.Parameters.AddWithValue("$document", document);

        // Round-trippable, and the same format a task's timestamps use, because it has
        // to mean the same thing: an instant that survives a read on another machine
        // unchanged. It was written from the table's first day so that replicating the
        // plan (local ADR 0018) found no generation of rows to back-seed, the way the
        // tasks table had to back-seed its own from created_at.
        //
        // Never at or before the stamp the row already has. A copy taken from a PC
        // whose clock runs ahead carries a stamp this clock has not reached, and an
        // edit stamped with plain "now" would then read as older than the plan it was
        // made on — refused by the replica, and overwritten by that plan on the next
        // pull. One tick past it keeps the edit the later version.
        var stamp = DateTimeOffset.UtcNow;
        if (await StampAsync(connection, cancellationToken).ConfigureAwait(false) is { } held && stamp <= held)
        {
            stamp = held.AddTicks(1);
        }

        command.Parameters.AddWithValue("$updated_at", Format(stamp));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // --- The replicated document (local ADR 0018) ---------------------------

    public RoadmapReplicaDocument Document => RoadmapReplicaDocument.Plan;

    /// <summary>The row's document and stamp exactly as stored, or <c>null</c> when
    /// no plan was ever saved here — a device that never planned has nothing to
    /// send.</summary>
    public async Task<RoadmapReplicaCopyDto?> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT document, updated_at FROM roadmap_plan WHERE id = $id;";
        command.Parameters.AddWithValue("$id", PlanRowId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        var document = reader.GetString(0);
        if (string.IsNullOrWhiteSpace(document)) return null;

        return new RoadmapReplicaCopyDto(document, Parse(reader.GetString(1)));
    }

    /// <summary>
    /// Stores another device's plan verbatim, at the stamp it arrived with — not this
    /// machine's clock, which would make the copy look newer than it is.
    /// <para>
    /// Refused, with nothing written, when the text is not a plan document: not JSON,
    /// not an object, no numeric <c>version</c> — every document this repository has
    /// ever written carries one — or a shape <see cref="LoadAsync"/> could not read.
    /// The trade <see cref="LoadAsync"/> makes, one step earlier: a plan is never
    /// overwritten with something unreadable.
    /// </para>
    /// </summary>
    public async Task<bool> TryWriteAsync(RoadmapReplicaCopyDto copy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(copy);

        if (!IsPlanDocument(copy.Content)) return false;

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO roadmap_plan (id, document, updated_at)
            VALUES ($id, $document, $updated_at)
            ON CONFLICT(id) DO UPDATE SET
                document = excluded.document,
                updated_at = excluded.updated_at;
            """;

        command.Parameters.AddWithValue("$id", PlanRowId);
        command.Parameters.AddWithValue("$document", copy.Content);
        command.Parameters.AddWithValue("$updated_at", Format(copy.UpdatedAt));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    private static bool IsPlanDocument(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;

        try
        {
            if (JsonNode.Parse(content) is not JsonObject root
                || !root.TryGetPropertyValue("version", out var version)
                || version is not JsonValue number
                || !number.TryGetValue<int>(out _))
            {
                return false;
            }

            return JsonSerializer.Deserialize<RoadmapPlanDocument>(content, RoadmapPlanDocument.JsonOptions) is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<DateTimeOffset?> StampAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT updated_at FROM roadmap_plan WHERE id = $id;";
        command.Parameters.AddWithValue("$id", PlanRowId);

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string stored
            ? Parse(stored)
            : null;
    }

    private static string Format(DateTimeOffset stamp) => stamp.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string stored) =>
        DateTimeOffset.Parse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    // --- Schema -------------------------------------------------------------

    /// <summary>Creates the database and this table if they are not there yet. Called
    /// on the way into every operation, for the reason
    /// <see cref="SqliteTaskRepository"/> gives: the statements are idempotent, and
    /// caching which paths have been prepared would be wrong the first time somebody
    /// moved or deleted the file underneath a running app.
    /// <para>
    /// This repository creates <em>only</em> its own table, and the task repository
    /// only creates its own. Inherited ADR 0014 puts persistence in the hands of the
    /// module that owns the data, and one adapter running another module's DDL would
    /// be the first crack in that — the two tables share a file, not an owner.
    /// </para>
    /// <para>
    /// Note which record covers this. Local ADR 0006 names three permitted shapes and
    /// all three are column-level — add a nullable column, seed it, rewrite a retired
    /// value — so creating a table is not one of them. It is covered by local ADR 0003
    /// instead, which put the schema statements behind idempotent <c>IF NOT EXISTS</c>
    /// DDL run on every open. An existing <c>backlog.db</c> full of tasks gains this
    /// table and loses nothing.
    /// </para></summary>
    private static Task<SqliteConnection> OpenAsync(string databasePath, CancellationToken cancellationToken) =>
        SqliteSchema.OpenAsync(databasePath, EnsureSchemaAsync, cancellationToken);

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await SqliteSchema.EnsureAsync(connection, """
            CREATE TABLE IF NOT EXISTS roadmap_plan (
                id         TEXT PRIMARY KEY NOT NULL,
                document   TEXT NOT NULL,
                -- NOT NULL, unlike the tasks table's updated_at. That one had to
                -- tolerate null because ALTER TABLE added it to a table that
                -- already had rows; this column ships with its table, so there has
                -- never been a row without one. ADR 0006's nullable-always rule is
                -- about columns added to a populated table, and this is not one.
                updated_at TEXT NOT NULL
            );
            """, cancellationToken).ConfigureAwait(false);

        // No index. The table holds one row and every read is by primary key, so
        // there is nothing an index could accelerate.
    }
}
