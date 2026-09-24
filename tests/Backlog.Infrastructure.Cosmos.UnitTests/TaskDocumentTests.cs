// What is testable about the Cosmos adapter without Cosmos: the shape of a
// document, the JSON contract that shape depends on, and the TTL rule.
//
// Two things are deliberately NOT tested here, and adding a test that appears to
// cover either would be worse than the gap. Change-feed delivery and ordering
// need a real feed — the emulator can give one, a unit test cannot, and a fake
// would only assert that the fake was written to match the code. TTL expiry is
// not reachable at all: the Cosmos emulator does not honour TTL, so expiry is
// deployed-only behaviour and nothing in this repository can observe it. What is
// pinned below is that the right documents carry a ttl and the rest carry none.

using System.Text.Json;
using Backlog.Infrastructure.Cosmos;
using Backlog.Infrastructure.Cosmos.Tasks;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

public class TaskDocumentTests
{
    private static readonly OwnerScope Scope = new(
        new OwnerId(Guid.Parse("2a1d1b8e-2f2c-4a4b-9c3d-5e6f70819293")),
        new DeviceId(Guid.Parse("3b2e2c9f-3a3d-4b5c-8d4e-6f708192a3b4")));

    private static readonly Guid TaskId = Guid.Parse("4c3f3daa-4b4e-4c6d-9e5f-708192a3b4c5");

    [Fact]
    public void A_change_round_trips_through_a_document()
    {
        var change = Change();

        var record = TaskDocumentFactory.ToRecord(TaskDocumentFactory.From(Scope, change, new CosmosOptions()));

        Assert.NotNull(record);
        Assert.Equal(change.Id, record.Change.Id);
        Assert.Equal(change.UpdatedAt, record.Change.UpdatedAt);
        Assert.Null(record.Change.DeletedAt);
        Assert.Equal(Scope.DeviceId.Value, record.DeviceId);
        Assert.Equal(change.Task, record.Change.Task);
    }

    /// <summary>
    /// The partition key path is <c>/ownerId</c> in the AppHost and in
    /// <c>infra/sync/main.bicep</c>, and nothing in either place fails when a
    /// document arrives without that property — it lands in the undefined
    /// partition instead. This assertion is the only thing standing between a
    /// change to the naming policy and a silently unpartitioned container.
    /// </summary>
    [Fact]
    public void The_document_serialises_with_the_names_the_container_is_partitioned_on()
    {
        var document = TaskDocumentFactory.From(Scope, Change(), new CosmosOptions());

        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(document, ReplicaDocumentSerialization.Options));

        var root = json.RootElement;

        Assert.Equal(TaskId.ToString("D"), root.GetProperty("id").GetString());
        Assert.Equal(Scope.OwnerId.Value.ToString("D"), root.GetProperty("ownerId").GetString());
        Assert.Equal(Scope.DeviceId.Value.ToString("D"), root.GetProperty("deviceId").GetString());
        Assert.True(root.TryGetProperty("task", out _));
    }

    /// <summary>The payload is stored whole and camelCased with everything else,
    /// so a device reading it back gets its own field names rather than the
    /// service's.</summary>
    [Fact]
    public void The_payload_is_stored_under_camel_cased_names()
    {
        var document = TaskDocumentFactory.From(Scope, Change(), new CosmosOptions());

        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(document, ReplicaDocumentSerialization.Options));

        var task = json.RootElement.GetProperty("task");

        Assert.Equal("Call the dentist", task.GetProperty("title").GetString());
        Assert.Equal("phone", task.GetProperty("sourceInboxId").GetString());
        Assert.Equal("draft", task.GetProperty("status").GetString());
    }

    /// <summary>
    /// A live task must carry no <c>ttl</c> property at all — not a null one.
    /// The container's <c>defaultTtl</c> is -1, so the feature is on and the
    /// absence of this property is the whole of what keeps a task forever.
    /// </summary>
    [Fact]
    public void A_live_task_is_written_without_a_ttl()
    {
        var document = TaskDocumentFactory.From(Scope, Change(), new CosmosOptions());

        Assert.Null(document.Ttl);

        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(document, ReplicaDocumentSerialization.Options));

        Assert.False(json.RootElement.TryGetProperty("ttl", out _));
        Assert.False(json.RootElement.TryGetProperty("deletedAt", out _));
    }

    [Fact]
    public void A_tombstone_is_written_with_the_configured_ttl()
    {
        var options = new CosmosOptions { TaskTombstoneTtlSeconds = 1_234 };
        var deleted = Change() with { DeletedAt = DateTimeOffset.UtcNow };

        var document = TaskDocumentFactory.From(Scope, deleted, options);

        Assert.Equal(1_234, document.Ttl);

        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(document, ReplicaDocumentSerialization.Options));

        Assert.Equal(1_234, json.RootElement.GetProperty("ttl").GetInt32());
    }

    /// <summary>Six months, and it is a bound rather than a preference: it is how
    /// long a device may be offline and still learn that a task was deleted
    /// instead of pushing its copy back.</summary>
    [Fact]
    public void The_default_tombstone_ttl_is_a_hundred_and_eighty_days()
    {
        Assert.Equal(TimeSpan.FromDays(180), TimeSpan.FromSeconds(new CosmosOptions().TaskTombstoneTtlSeconds));
    }

    /// <summary>The defaults name the database and container the AppHost and the
    /// bicep already create, so an unconfigured deployment is a correctly
    /// configured one rather than a broken one.</summary>
    [Fact]
    public void The_defaults_name_what_the_app_model_creates()
    {
        var options = new CosmosOptions();

        Assert.Equal("backlog", options.DatabaseName);
        Assert.Equal("tasks", options.TasksContainerName);
    }

    /// <summary>One bad document must not stop an owner syncing, so the mapping
    /// answers null and the adapter drops it rather than throwing through the
    /// whole page.</summary>
    [Fact]
    public void A_document_that_is_not_one_of_ours_maps_to_nothing()
    {
        Assert.Null(TaskDocumentFactory.ToRecord(new TaskDocument { Id = "not-a-guid" }));

        Assert.Null(TaskDocumentFactory.ToRecord(new TaskDocument
        {
            Id = TaskId.ToString("D"),
            DeviceId = Scope.DeviceId.Value.ToString("D"),
            Task = null,
        }));
    }

    /// <summary>
    /// A field this build has no member for survives the service. The path is the
    /// whole of it: a newer device's push read the way the endpoint reads it, the
    /// document written and read back the way the store does, and the change
    /// written out the way a pull hands it back. A service built before the field
    /// existed once dropped <c>completedOn</c> on exactly this path, and every other
    /// device then overwrote the tick with the copy it had been handed.
    /// </summary>
    [Fact]
    public void A_field_this_build_does_not_know_survives_the_round_trip()
    {
        var wire = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var pushed = JsonSerializer.SerializeToNode(Change(), wire)!;
        pushed["task"]!["reviewedOn"] = "2026-09-24";
        pushed["task"]!["subItems"]![0]!["estimate"] = 3;

        var received = pushed.Deserialize<TaskChange>(wire)!;

        var stored = JsonSerializer.Deserialize<TaskDocument>(
            JsonSerializer.Serialize(TaskDocumentFactory.From(Scope, received, new CosmosOptions()), ReplicaDocumentSerialization.Options),
            ReplicaDocumentSerialization.Options)!;

        using var pulled = JsonDocument.Parse(
            JsonSerializer.Serialize(TaskDocumentFactory.ToRecord(stored)!.Change, wire));

        var task = pulled.RootElement.GetProperty("task");

        Assert.Equal("2026-09-24", task.GetProperty("reviewedOn").GetString());
        Assert.Equal(3, task.GetProperty("subItems")[0].GetProperty("estimate").GetInt32());
        Assert.Equal("Call the dentist", task.GetProperty("title").GetString());
    }

    /// <summary>A payload with nothing unrecognised carries nothing extra, so a
    /// payload a device builds still equals the same payload read back.</summary>
    [Fact]
    public void A_payload_with_nothing_unrecognised_carries_no_extension_data()
    {
        var wire = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var read = JsonSerializer.Deserialize<TaskChange>(JsonSerializer.Serialize(Change(), wire), wire)!;

        Assert.Null(read.Task.Unrecognised);
        Assert.Null(read.Task.SubItems[0].Unrecognised);
    }

    private static TaskChange Change() => new(
        TaskId,
        new DateTimeOffset(2026, 9, 7, 10, 30, 0, TimeSpan.Zero),
        DeletedAt: null,
        new TaskPayload(
            "Call the dentist",
            ContentMd: "Ask about the crown.",
            Type: "task",
            Status: "draft",
            Priority: "medium",
            Order: 3,
            Area: "home",
            new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero),
            SourceInboxId: "phone",
            RecurrenceSourceId: null,
            DueOn: new DateOnly(2026, 9, 30),
            RemindAt: null,
            Recurrence: "weekdays",
            InMyDayOn: null,
            View: "steps",
            Effort: 2,
            ImportPlanId: null,
            ImportItemId: null,
            AttachmentPath: null,
            Tags: ["health"],
            RepoIds: [],
            DependsOn: [],
            SubItems: [new SubItemPayload(Guid.Parse("5d4a4ebb-5c5f-4d7e-af60-8192a3b4c5d6"), "Find the number", "pending", null, 0)],
            UsageEvents: [],
            ProjectionRefs: []));
}
