using System.Globalization;
using System.Text.Json;

using Backlog.Infrastructure.Sqlite.Roadmap;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The stored roadmap plan. What these assert is that a plan put in comes back out
/// unchanged, and that the shape it is stored in stays the shape it was — the plan
/// moved out of <c>_roadmap/plan.json</c> and into one row of <c>backlog.db</c>, and
/// a change of medium that quietly changed the content would be two changes wearing
/// one commit.
/// <para>
/// The assertions that used to read the file now read the <c>document</c> column,
/// through the helpers at the bottom. Reading the column rather than trusting a
/// round trip is the point of them: a serializer that writes a date as an instant
/// still round-trips perfectly inside one process, and only the stored bytes say
/// what another reader would see.
/// </para>
/// </summary>
public sealed class SqliteRoadmapPlanRepositoryTests : IDisposable
{
    private readonly string _dir;
    private readonly SqliteRoadmapPlanRepository _plans;

    public SqliteRoadmapPlanRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "roadmap-tests-" + Guid.NewGuid().ToString("N"));
        _plans = new SqliteRoadmapPlanRepository(_dir);
    }

    private static PlannedWindow Window(int startDay, int endDay) =>
        PlannedWindow.Of(new DateOnly(2026, 1, startDay), new DateOnly(2026, 1, endDay));

    [Fact]
    public async Task AFirstRunLoadsAnEmptyPlan_RatherThanFailing()
    {
        var plan = await _plans.LoadAsync();

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void ThePlanLivesUnderTheStorageRoot_InTheSameFileAsTheBacklog()
    {
        // Not beside the backlog any more — inside it. One database is one thing to
        // back up, one file for a sync product to conflict over instead of two, and
        // one place a workspace root actually points at.
        Assert.Equal(Path.Combine(_dir, "backlog.db"), _plans.DatabasePath);
    }

    [Fact]
    public async Task NoRoadmapFolderIsCreatedUnderTheRoot()
    {
        var plan = RoadmapPlan.Empty();
        plan.AddItem("Anything", Window(5, 9));

        await _plans.SaveAsync(plan);

        // The folder the plan used to live in has no reason to exist now, and one that
        // appeared empty beside the database would invite somebody to look for a plan
        // in it. Storing the plan in the database means storing it only there.
        Assert.False(Directory.Exists(Path.Combine(_dir, "_roadmap")));
    }

    /// <summary>
    /// The link to a task is written as <c>backlogEntryId</c>, the name it had before
    /// the Backlog bounded context was renamed to Tasks.
    /// <para>
    /// The C# property is <c>TaskId</c> now, and under this document's camelCase policy
    /// it would serialize as <c>taskId</c> without the explicit
    /// <c>JsonPropertyName</c> holding it. That would not fail: every plan already
    /// stored would still load, just with every task link quietly gone. A rename that
    /// loses data loudly gets fixed; one that loses it silently ships.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ThePlanKeepsTheKeyItWasWrittenWith_SoOlderPlansStillResolveTheirLinks()
    {
        var taskId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var plan = RoadmapPlan.Empty();
        plan.AddItem("Linked", Window(5, 9), taskId: taskId);

        await _plans.SaveAsync(plan);
        var json = await StoredDocumentAsync();

        Assert.Contains("\"backlogEntryId\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"taskId\"", json, StringComparison.Ordinal);
    }

    /// <summary>A plan written before the rename still loads with its link intact.</summary>
    [Fact]
    public async Task APlanWrittenBeforeTheRename_StillLoadsItsTaskLink()
    {
        var taskId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var itemId = Guid.NewGuid();
        var json = $$"""
            {
              "version": 1,
              "items": [
                {
                  "id": "{{itemId}}",
                  "title": "Written before the rename",
                  "start": "2026-01-05",
                  "end": "2026-01-09",
                  "backlogEntryId": "{{taskId}}"
                }
              ]
            }
            """;
        await StoreDocumentAsync(json);

        var loaded = await _plans.LoadAsync();

        Assert.Equal(taskId, loaded.Items.Single(item => item.Id == itemId).TaskId);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsTheWholePlan()
    {
        var plan = RoadmapPlan.Empty();
        var design = plan.AddItem(
            "Design the sync service",
            Window(5, 9),
            PlanningPriority.High,
            RepositoryScope.Of(["backlog", "fincent"]),
            PlanningLane.Of("platform"),
            taskId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            notes: "Spike first.").Value;
        var build = plan.AddItem("Build it", Window(12, 23)).Value;
        var release = plan.AddMilestone("1.0", new DateOnly(2026, 2, 2), MilestoneKind.Release).Value;
        plan.AddDependency(build.Id, design.Id);
        plan.AddDependency(release.Id, build.Id);

        await _plans.SaveAsync(plan);
        var loaded = await _plans.LoadAsync();

        var loadedDesign = loaded.Items.Single(item => item.Id == design.Id);
        Assert.Equal("Design the sync service", loadedDesign.Title);
        Assert.Equal(new DateOnly(2026, 1, 5), loadedDesign.Window.Start);
        Assert.Equal(new DateOnly(2026, 1, 9), loadedDesign.Window.End);
        Assert.Equal(PlanningPriority.High, loadedDesign.Priority);
        Assert.Equal(["backlog", "fincent"], loadedDesign.Scope.Aliases);
        Assert.Equal("platform", loadedDesign.Lane.Name);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), loadedDesign.TaskId);
        Assert.Equal("Spike first.", loadedDesign.Notes);

        var loadedBuild = loaded.Items.Single(item => item.Id == build.Id);
        Assert.Equal([design.Id], loadedBuild.Dependencies.All);

        var loadedRelease = Assert.Single(loaded.Milestones);
        Assert.Equal(new DateOnly(2026, 2, 2), loadedRelease.On);
        Assert.Equal(MilestoneKind.Release, loadedRelease.Kind);
        Assert.Equal([build.Id], loadedRelease.Dependencies.All);
    }

    [Fact]
    public async Task DatesAreWrittenAsPlainDays_WithNoTimeAndNoOffset()
    {
        var plan = RoadmapPlan.Empty();
        plan.AddItem("Anything", Window(5, 9));

        await _plans.SaveAsync(plan);
        var json = await StoredDocumentAsync();

        Assert.Contains("\"start\":\"2026-01-05\"", json, StringComparison.Ordinal);
        Assert.Contains("\"end\":\"2026-01-09\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("T00:00:00", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnumsAreWrittenAsWords_SoAReorderedEnumCannotReinterpretAPlan()
    {
        var plan = RoadmapPlan.Empty();
        plan.AddItem("Critical work", Window(5, 9), PlanningPriority.Critical);
        plan.AddMilestone("Freeze", new DateOnly(2026, 2, 2), MilestoneKind.Freeze);

        await _plans.SaveAsync(plan);
        var json = await StoredDocumentAsync();

        Assert.Contains("\"priority\":\"critical\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"freeze\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AJsonSyntaxErrorInTheStoredDocumentNeverStopsTheAppOpening()
    {
        var plan = RoadmapPlan.Empty();
        plan.AddItem("Something", Window(5, 9));
        await _plans.SaveAsync(plan);

        await StoreDocumentAsync("{ \"items\": [ oops");
        var storedAt = await StoredUpdatedAtAsync();

        var loaded = await _plans.LoadAsync();

        Assert.True(loaded.IsEmpty);

        // And the broken row is left exactly as it was — document and timestamp both —
        // so whatever wrote it can still be worked out. Rewriting it here would turn a
        // bad write into data loss, which is the trade the file version made and the
        // reason it survives the move into the database.
        Assert.Contains("oops", await StoredDocumentAsync(), StringComparison.Ordinal);
        Assert.Equal(storedAt, await StoredUpdatedAtAsync());
    }

    [Fact]
    public async Task AHandWrittenPlanIsRead()
    {
        var itemId = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new
        {
            version = 1,
            items = new[]
            {
                new
                {
                    id = itemId.ToString(),
                    title = "Typed by hand",
                    start = "2026-03-02",
                    end = "2026-03-13",
                    priority = "high",
                    repositories = new[] { "Backlog" },
                    lane = "platform",
                    dependsOn = Array.Empty<string>()
                }
            }
        });
        await StoreDocumentAsync(json);

        var loaded = await _plans.LoadAsync();

        var item = Assert.Single(loaded.Items);
        Assert.Equal("Typed by hand", item.Title);
        Assert.Equal(new DateOnly(2026, 3, 2), item.Window.Start);
        Assert.Equal(PlanningPriority.High, item.Priority);
        Assert.Equal(["backlog"], item.Scope.Aliases);
    }

    [Fact]
    public async Task ABlockWithNoUsableIdOrTitle_CostsOnlyItsOwnEntry()
    {
        var good = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new
        {
            version = 1,
            items = new object[]
            {
                new { id = "not-a-guid", title = "No id", start = "2026-03-02", end = "2026-03-02" },
                new { id = Guid.NewGuid().ToString(), title = "", start = "2026-03-02", end = "2026-03-02" },
                new { id = good.ToString(), title = "Fine", start = "2026-03-02", end = "2026-03-06" }
            }
        });
        await StoreDocumentAsync(json);

        var loaded = await _plans.LoadAsync();

        var item = Assert.Single(loaded.Items);
        Assert.Equal(good, item.Id);
    }

    [Fact]
    public async Task APlanWideDateAndItsBandColoursRoundTrip()
    {
        // The colours are legacy — nothing writes them any more — but a plan that
        // already has them has to keep them until they have been carried over to the
        // repository they were chosen for.
        var plan = RoadmapPlan.Rehydrate([], [], BandColours.Of([new KeyValuePair<string, int>("backlog", 4)]));
        var freeze = plan.AddMilestone(
            "Freeze",
            new DateOnly(2026, 2, 2),
            MilestoneKind.Freeze,
            isPlanWide: true).Value;
        plan.AddMilestone("1.0", new DateOnly(2026, 3, 31));

        await _plans.SaveAsync(plan);
        var loaded = await _plans.LoadAsync();

        Assert.True(loaded.Milestones.Single(milestone => milestone.Id == freeze.Id).IsPlanWide);
        Assert.False(loaded.Milestones.Single(milestone => milestone.Title == "1.0").IsPlanWide);
        Assert.Equal(4, loaded.BandColours.For("backlog"));
    }

    [Fact]
    public async Task ATagAndKnowledgeReferencesRoundTrip()
    {
        var plan = RoadmapPlan.Empty();
        var item = plan.AddItem(
            "Extract the sync service",
            Window(5, 9),
            tag: PlanningTag.Of("sync"),
            knowledgeRefs: KnowledgeReferences.Of(
                [".domain/tasks/domain.md#aggregate-backlog-entry", ".tech/technology-graph.md"])).Value;

        await _plans.SaveAsync(plan);
        var loaded = await _plans.LoadAsync();

        var loadedItem = loaded.Items.Single(candidate => candidate.Id == item.Id);
        Assert.Equal("sync", loadedItem.Tag.Value);
        Assert.Equal(
            [".domain/tasks/domain.md#aggregate-backlog-entry", ".tech/technology-graph.md"],
            loadedItem.KnowledgeRefs.Refs);
    }

    [Fact]
    public async Task AHandWrittenPlanWithNoTagStillLoads_AndGetsOneDerivedFromItsTitle()
    {
        // A plan from before tags existed: no tag field, no knowledge field. It has to
        // load, and every item still comes back with a tag.
        var json = JsonSerializer.Serialize(new
        {
            version = 1,
            items = new[]
            {
                new
                {
                    id = Guid.NewGuid().ToString(),
                    title = "Typed by hand",
                    start = "2026-03-02",
                    end = "2026-03-13",
                    priority = "high"
                }
            }
        });
        await StoreDocumentAsync(json);

        var loaded = await _plans.LoadAsync();

        var item = Assert.Single(loaded.Items);
        Assert.Equal("typed-by-hand", item.Tag.Value);
        Assert.True(item.KnowledgeRefs.IsEmpty);
    }

    [Fact]
    public async Task AnEmptyKnowledgeListIsOmittedFromTheStoredDocument()
    {
        var plan = RoadmapPlan.Empty();
        plan.AddItem("Points at nothing", Window(5, 9), tag: PlanningTag.Of("plain"));

        await _plans.SaveAsync(plan);
        var json = await StoredDocumentAsync();

        // An empty array on every item is a key that says nothing, and a document
        // nobody opens by hand has even less use for one than the file did. The tag is
        // always written, because every item has one.
        Assert.DoesNotContain("knowledge", json, StringComparison.Ordinal);
        Assert.Contains("\"tag\":\"plain\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOrdinaryDateCostsNoKeyInTheStoredDocument()
    {
        var plan = RoadmapPlan.Empty();
        plan.AddMilestone("1.0", new DateOnly(2026, 3, 31));

        await _plans.SaveAsync(plan);
        var json = await StoredDocumentAsync();

        // A false on every milestone is a key that says nothing.
        Assert.DoesNotContain("planWide", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BandColoursAreStoredAsWhichOneRatherThanAsAColour()
    {
        var plan = RoadmapPlan.Rehydrate([], [], BandColours.Of([new KeyValuePair<string, int>("backlog", 3)]));
        plan.AddMilestone("1.0", new DateOnly(2026, 3, 31));

        await _plans.SaveAsync(plan);
        var json = await StoredDocumentAsync();

        Assert.Contains("\"backlog\":3", json, StringComparison.Ordinal);

        // Which hue that is belongs to the stylesheet. A stored plan naming one would be
        // a plan inventing a colour, which the design system does not allow it to do.
        Assert.DoesNotContain("#", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStoredColourOutsideTheSanctionedSetIsDroppedOnLoad()
    {
        var json = JsonSerializer.Serialize(new
        {
            version = 1,
            items = Array.Empty<object>(),
            milestones = new[]
            {
                new { id = Guid.NewGuid().ToString(), title = "1.0", on = "2026-03-31", kind = "release" }
            },
            bands = new Dictionary<string, int> { ["backlog"] = 9, ["fincent"] = 2 }
        });
        await StoreDocumentAsync(json);

        var loaded = await _plans.LoadAsync();

        Assert.Null(loaded.BandColours.For("backlog"));
        Assert.Equal(2, loaded.BandColours.For("fincent"));
    }

    [Fact]
    public async Task EverySaveStampsAnInstantThatSurvivesTheRoundTrip_AndTheNextSaveMovesItOn()
    {
        var plan = RoadmapPlan.Empty();
        plan.AddItem("Anything", Window(5, 9));

        await _plans.SaveAsync(plan);
        var first = await StoredUpdatedAtAsync();

        // Round-trippable, not merely present. Nothing reads the column yet; it is
        // written from the table's first day so that when sync does come to the plan
        // there is no generation of rows to apologise for, the way the tasks table had
        // to back-seed its own updated_at from created_at.
        Assert.NotNull(first);
        Assert.True(
            DateTimeOffset.TryParseExact(
                first, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var firstStamp),
            $"updated_at was not round-trippable: '{first}'.");

        plan.AddItem("And another", Window(12, 16));
        await _plans.SaveAsync(plan);

        var second = await StoredUpdatedAtAsync();
        Assert.True(
            DateTimeOffset.TryParseExact(
                second, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var secondStamp),
            $"updated_at was not round-trippable: '{second}'.");
        Assert.True(
            secondStamp > firstStamp,
            $"The second save left updated_at at '{second}', not after '{first}'.");
    }

    [Fact]
    public async Task SavingTwiceLeavesOneRow_BecauseAWorkspacePlansOneRoadmap()
    {
        var plan = RoadmapPlan.Empty();
        plan.AddItem("First", Window(5, 9));
        await _plans.SaveAsync(plan);

        plan.AddItem("Second", Window(12, 16));
        await _plans.SaveAsync(plan);

        // The save is an UPSERT on a constant id rather than an insert, so a plan
        // edited a hundred times is still one row. A second row would be a second plan
        // nothing would ever read, and the first one silently frozen.
        Assert.Equal(1, await PlanRowCountAsync());
        Assert.Equal(2, (await _plans.LoadAsync()).Items.Count);
    }

    [Fact]
    public async Task ThePlanAndTheTasksShareOneFileWithoutDisturbingEachOther()
    {
        var tasks = new SqliteTaskRepository(_dir);
        var task = new TaskItem("Ship it", "Body.", EntryType.Task);
        await tasks.SaveAsync(task);

        var plan = RoadmapPlan.Empty();
        plan.AddItem("Plan it", Window(5, 9));
        await _plans.SaveAsync(plan);

        // Two tables with an owner each, in one file. Neither adapter reads, writes or
        // creates the other's table — what they share is a database, not a schema — so
        // the thing worth asserting is that sharing it costs neither of them anything.
        var loadedTask = await tasks.GetAsync(task.Id);
        Assert.NotNull(loadedTask);
        Assert.Equal("Ship it", loadedTask.Title);
        Assert.Equal("Plan it", Assert.Single((await _plans.LoadAsync()).Items).Title);

        Assert.Equal(_plans.DatabasePath, tasks.DatabasePath);
        Assert.Single(
            Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories),
            file => Path.GetFileName(file) == "backlog.db");
    }

    [Fact]
    public async Task ThePlanTableIsAddedToADatabaseTheTasksAlreadyMade_AndCostsThemNothing()
    {
        // The upgrade path for everyone who already has a backlog.db: the tasks table
        // is there, roadmap_plan is not, and the plan repository has to create its own
        // table on the way in rather than expecting one. Local ADR 0003's idempotent
        // open is what makes that a non-event; this is the test that says so.
        var tasks = new SqliteTaskRepository(_dir);
        var task = new TaskItem("Was here first", "Body.", EntryType.Task);
        await tasks.SaveAsync(task);
        Assert.False(await TableExistsAsync("roadmap_plan"));

        var plan = RoadmapPlan.Empty();
        plan.AddItem("Arrived later", Window(5, 9));
        await _plans.SaveAsync(plan);

        Assert.Equal("Arrived later", Assert.Single((await _plans.LoadAsync()).Items).Title);
        Assert.Equal("Was here first", Assert.Single(await tasks.ListAsync()).Title);
    }

    [Fact]
    public async Task PointingTheRootedRepositoryAtAnotherFolder_MovesToThatPlan()
    {
        var second = Path.Combine(Path.GetTempPath(), "roadmap-tests-" + Guid.NewGuid().ToString("N"));
        var root = _dir;
        var rooted = new RootedSqliteRoadmapPlanRepository(() => root);

        var first = RoadmapPlan.Empty();
        first.AddItem("In the first folder", Window(5, 9));
        await rooted.SaveAsync(first);

        root = second;
        try
        {
            var loaded = await rooted.LoadAsync();

            Assert.True(loaded.IsEmpty);
            Assert.Equal(Path.Combine(second, "backlog.db"), rooted.DatabasePath);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(second)) Directory.Delete(second, recursive: true);
        }
    }

    // --- Reading the row the assertions used to read out of a file ----------

    /// <summary>The stored plan document, exactly as another reader would find it.</summary>
    private async Task<string?> StoredDocumentAsync() =>
        await ScalarAsync("SELECT document FROM roadmap_plan WHERE id = $id;");

    /// <summary>The stored timestamp, as written rather than as parsed, so a test can
    /// say what format it is in.</summary>
    private async Task<string?> StoredUpdatedAtAsync() =>
        await ScalarAsync("SELECT updated_at FROM roadmap_plan WHERE id = $id;");

    private async Task<int> PlanRowCountAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM roadmap_plan;";

        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private async Task<bool> TableExistsAsync(string name)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", name);

        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>
    /// Puts a document into the row by hand — the equivalent of the editor the plan
    /// file used to be opened in, and the only way left to stage a plan the app itself
    /// would never write.
    /// <para>
    /// It creates the table if it is not there, because these tests stage documents
    /// against a root nothing has saved to yet. Repeating the DDL rather than reaching
    /// into the repository for it is deliberate: a helper that called the production
    /// bootstrap could not tell the difference between a schema that is right and one
    /// that is merely self-consistent.
    /// </para>
    /// </summary>
    private async Task StoreDocumentAsync(string document)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS roadmap_plan (
                id         TEXT PRIMARY KEY NOT NULL,
                document   TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            INSERT INTO roadmap_plan (id, document, updated_at)
            VALUES ($id, $document, $updated_at)
            ON CONFLICT(id) DO UPDATE SET
                document = excluded.document,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", SqliteRoadmapPlanRepository.PlanRowId);
        command.Parameters.AddWithValue("$document", document);
        command.Parameters.AddWithValue(
            "$updated_at", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync();
    }

    private async Task<string?> ScalarAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", SqliteRoadmapPlanRepository.PlanRowId);

        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : (string)value;
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        Directory.CreateDirectory(_dir);

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _plans.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

        await connection.OpenAsync();
        return connection;
    }

    public void Dispose()
    {
        // The connection pool can still hold the file open the instant a test ends, and
        // a temp folder that will not delete is not a failure worth failing a green
        // suite over.
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_dir)) return;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
