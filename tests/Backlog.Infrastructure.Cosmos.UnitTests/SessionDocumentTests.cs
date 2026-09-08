// What is testable about the session adapter without Cosmos: the shape of a
// document, the JSON contract the container's indexing policy depends on, and
// the composition of the key that makes one machine unable to address another's
// record.
//
// Two things are deliberately NOT tested here. Change-feed delivery and ordering
// need a real feed — the emulator can give one, a unit test cannot, and a fake
// would only assert that the fake was written to match the code. Retention is
// not reachable either, and for a different reason than it is on the tasks
// container: there is no per-document ttl to assert, because twelve months is
// `defaultTtl` on the container in infra/sync/main.bicep. What is pinned below
// is that this service writes no ttl at all, which is what leaves that setting
// in force.

using System.Text.Json;
using Backlog.Infrastructure.Cosmos.Sessions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

public class SessionDocumentTests
{
    private static readonly OwnerScope Scope = new(
        new OwnerId(Guid.Parse("2a1d1b8e-2f2c-4a4b-9c3d-5e6f70819293")),
        new DeviceId(Guid.Parse("3b2e2c9f-3a3d-4b5c-8d4e-6f708192a3b4")));

    private static readonly OwnerScope OtherMachine = new(
        Scope.OwnerId,
        new DeviceId(Guid.Parse("4c3f3daa-4b4e-4c6d-9e5f-708192a3b4c5")));

    [Fact]
    public void A_record_round_trips_through_a_document()
    {
        var record = Record();

        var entry = SessionDocumentFactory.ToEntry(SessionDocumentFactory.From(Scope, record));

        Assert.NotNull(entry);
        Assert.Equal(record, entry.Record);
        Assert.Equal(Scope.DeviceId.Value, entry.MachineId);
    }

    /// <summary>
    /// The three optional fields are optional all the way down. A session that
    /// ran outside a repository, on a detached head, under an agent that recorded
    /// no start time is an ordinary session and not a defective record — and a
    /// null that came back as an empty string would show as a blank branch rather
    /// than as no branch.
    /// </summary>
    [Fact]
    public void The_three_unknowable_fields_round_trip_as_null()
    {
        var sparse = Record() with { RepositoryAlias = null, Branch = null, StartedAt = null };

        var entry = SessionDocumentFactory.ToEntry(SessionDocumentFactory.From(Scope, sparse));

        Assert.NotNull(entry);
        Assert.Null(entry.Record.RepositoryAlias);
        Assert.Null(entry.Record.Branch);
        Assert.Null(entry.Record.StartedAt);
    }

    /// <summary>
    /// <c>infra/sync/main.bicep</c> indexes the sessions container on exactly
    /// <c>/ownerId/?</c>, <c>/machineId/?</c>, <c>/repositoryAlias/?</c>,
    /// <c>/startedAt/?</c> and <c>/lastActivityAt/?</c> and excludes <c>/*</c>.
    /// Every one of those is a root path, and Cosmos does not object to an
    /// included path no document has — it just indexes nothing. This assertion is
    /// the only thing standing between a renamed property, or a record nested one
    /// level down, and five indexes over nothing.
    /// </summary>
    [Theory]
    [InlineData("ownerId")]
    [InlineData("machineId")]
    [InlineData("repositoryAlias")]
    [InlineData("startedAt")]
    [InlineData("lastActivityAt")]
    public void Every_indexed_path_in_the_bicep_is_a_root_property_of_the_document(string path)
    {
        using var json = Serialized(SessionDocumentFactory.From(Scope, Record()));

        Assert.True(json.RootElement.TryGetProperty(path, out _));
    }

    /// <summary>The partition key path is <c>/ownerId</c> in the AppHost and in
    /// the bicep, and nothing in either place fails when a document arrives
    /// without that property — it lands in the undefined partition
    /// instead.</summary>
    [Fact]
    public void The_document_serialises_with_the_names_the_container_is_partitioned_on()
    {
        using var json = Serialized(SessionDocumentFactory.From(Scope, Record()));

        var root = json.RootElement;

        Assert.Equal(Scope.OwnerId.Value.ToString("D"), root.GetProperty("ownerId").GetString());
        Assert.Equal(Scope.DeviceId.Value.ToString("D"), root.GetProperty("machineId").GetString());
    }

    /// <summary>
    /// The whole whitelist, and nothing beside it. Written as a set comparison
    /// rather than as ten assertions because the failure worth catching is the
    /// eleventh property somebody adds — .arc42/adr/0005 §Session records says a
    /// field not in its table does not sync, and a test that only checked the ten
    /// were present would pass with a transcript path beside them.
    /// </summary>
    [Fact]
    public void The_document_carries_the_whitelist_and_nothing_else()
    {
        using var json = Serialized(SessionDocumentFactory.From(Scope, Record()));

        var written = json.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                // The ten ADR 0005 permits...
                "sessionId", "agentKind", "machineId", "machineName", "repositoryAlias",
                "branch", "startedAt", "lastActivityAt", "turnCount", "durationSeconds",

                // ...plus the owner, which is the partition rather than a fact
                // about the session, and the document id Cosmos requires.
                "ownerId", "id",

                // ...plus Cosmos's own write stamp, which the store overwrites on
                // arrival and which is what orders the change feed. `_etag` is
                // null on a document this service built and nulls are not
                // written, so it is absent here and present on anything read
                // back.
                "_ts",
            },
            written);
    }

    /// <summary>
    /// Retention here is the container's, not the service's. The tasks container
    /// runs <c>defaultTtl: -1</c> and expires only the documents that ask to; the
    /// sessions container runs <c>defaultTtl: sessionRetentionSeconds</c> and
    /// expires every record at twelve months. A <c>ttl</c> written by this
    /// service would override that container setting for every record it wrote,
    /// and the emulator does not honour TTL, so nothing local would ever show it.
    /// </summary>
    [Fact]
    public void No_session_record_is_written_with_a_ttl()
    {
        using var json = Serialized(SessionDocumentFactory.From(Scope, Record()));

        Assert.False(json.RootElement.TryGetProperty("ttl", out _));
    }

    /// <summary>
    /// .domain/sessions/naming.md#session-identity puts a session's identity at
    /// the agent plus the id that agent issued. Two agents may issue the same
    /// string, and a key without the agent would collapse them into one record —
    /// one row where there were two.
    /// </summary>
    [Fact]
    public void Two_agents_issuing_one_session_id_are_two_documents()
    {
        var claude = SessionDocumentFactory.From(Scope, Record() with { AgentKind = "claude" });
        var copilot = SessionDocumentFactory.From(Scope, Record() with { AgentKind = "copilot" });

        Assert.NotEqual(claude.Id, copilot.Id);
    }

    /// <summary>
    /// The machine id leads the key, so a machine can only ever address documents
    /// beginning with its own device id. Two machines that somehow reported the
    /// same session get two documents rather than one overwriting the other,
    /// which is .arc42/adr/0005's single-writer rule held structurally rather than
    /// by the stamping check alone.
    /// </summary>
    [Fact]
    public void Two_machines_reporting_one_session_id_are_two_documents()
    {
        var record = Record();

        var mine = SessionDocumentFactory.From(Scope, record);
        var theirs = SessionDocumentFactory.From(OtherMachine, record);

        Assert.NotEqual(mine.Id, theirs.Id);
        Assert.StartsWith(Scope.DeviceId.Value.ToString("D"), mine.Id, StringComparison.Ordinal);
        Assert.StartsWith(OtherMachine.DeviceId.Value.ToString("D"), theirs.Id, StringComparison.Ordinal);
    }

    /// <summary>The defaults name the container the AppHost and the bicep already
    /// create. The bicep provisions <c>Sync__Cosmos__SessionsContainerName</c>,
    /// so an option that did not exist would have been an environment variable
    /// binding to nothing.</summary>
    [Fact]
    public void The_default_names_the_container_the_app_model_creates()
    {
        Assert.Equal("sessions", new CosmosOptions().SessionsContainerName);
    }

    /// <summary>One bad document must not stop an owner syncing, so the mapping
    /// answers null and the adapter drops it rather than throwing through the
    /// whole page. A record missing either half of its identity is the case that
    /// matters: it could not have been written by this service, because the edge
    /// refuses both.</summary>
    [Fact]
    public void A_document_that_is_not_one_of_ours_maps_to_nothing()
    {
        Assert.Null(SessionDocumentFactory.ToEntry(new SessionDocument { MachineId = "not-a-guid" }));

        Assert.Null(SessionDocumentFactory.ToEntry(new SessionDocument
        {
            MachineId = Scope.DeviceId.Value.ToString("D"),
            SessionId = string.Empty,
            AgentKind = "claude",
        }));

        Assert.Null(SessionDocumentFactory.ToEntry(new SessionDocument
        {
            MachineId = Scope.DeviceId.Value.ToString("D"),
            SessionId = "abc",
            AgentKind = string.Empty,
        }));
    }

    private static JsonDocument Serialized(SessionDocument document) =>
        JsonDocument.Parse(JsonSerializer.Serialize(document, ReplicaDocumentSerialization.Options));

    private static SessionRecord Record() => new(
        SessionId: "01994f2c-6e51-7a90-9b2f-3c4d5e6f7081",
        AgentKind: "claude",
        MachineName: "JS-DESKTOP",
        RepositoryAlias: "backlog",
        Branch: "main",
        StartedAt: new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero),
        LastActivityAt: new DateTimeOffset(2026, 9, 8, 10, 30, 0, TimeSpan.Zero),
        TurnCount: 42,
        DurationSeconds: 5_400);
}
