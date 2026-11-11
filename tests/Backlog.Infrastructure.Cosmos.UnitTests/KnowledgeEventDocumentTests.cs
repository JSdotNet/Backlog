// What is testable about the knowledge event adapter without Cosmos: the shape of a
// document, the names the container is partitioned on, that nothing about it expires, that
// the event itself round-trips unread, and that the host composes it. Change-feed delivery
// and the etag compare-and-write need an emulator, for the reasons TaskDocumentTests gives at
// its head.

using System.Text.Json;
using Backlog.Infrastructure.Cosmos.Extensions;
using Backlog.Infrastructure.Cosmos.KnowledgeEvents;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Extensions;
using Backlog.Modules.Sync.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

public class KnowledgeEventDocumentTests
{
    private static readonly OwnerScope Scope = new(
        new OwnerId(Guid.Parse("2a1d1b8e-2f2c-4a4b-9c3d-5e6f70819293")),
        new DeviceId(Guid.Parse("3b2e2c9f-3a3d-4b5c-8d4e-6f708192a3b4")));

    private static readonly Guid EventId = Guid.Parse("0193e0a4-6d60-7e8f-b071-92a3b4c5d6e7");
    private static readonly DateTimeOffset Noon = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private const string Event = """{"title":"NDC Oslo 2026","state":"Attended","noteLinks":[{"kind":"Talk","title":"Keynote"}],"somethingNew":{"nested":[1,2]}}""";

    [Fact]
    public void A_change_round_trips_through_a_document_with_its_event_unread()
    {
        var change = Change();

        var record = KnowledgeEventDocumentFactory.ToRecord(KnowledgeEventDocumentFactory.From(Scope, change));

        Assert.NotNull(record);
        Assert.Equal(EventId, record.Change.Id);
        Assert.Equal(KnowledgeEventKinds.KnowledgeEvent, record.Change.Kind);
        Assert.Equal(Noon, record.Change.UpdatedAt);
        Assert.True(JsonElement.DeepEquals(Json(Event), record.Change.Document));
        Assert.Equal(Scope.DeviceId.Value, record.DeviceId);
    }

    /// <summary>Through the serializer the container client uses, and back: the document a
    /// Cosmos read hands the replica.</summary>
    [Fact]
    public void A_stored_document_reads_back_as_the_change_that_wrote_it()
    {
        var json = JsonSerializer.Serialize(KnowledgeEventDocumentFactory.From(Scope, Change()), ReplicaDocumentSerialization.Options);

        var stored = JsonSerializer.Deserialize<KnowledgeEventDocument>(json, ReplicaDocumentSerialization.Options)!;
        var record = KnowledgeEventDocumentFactory.ToRecord(stored);

        Assert.NotNull(record);
        Assert.True(JsonElement.DeepEquals(Json(Event), record.Change.Document));
    }

    [Fact]
    public void The_document_serialises_with_the_names_the_container_is_partitioned_on()
    {
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(KnowledgeEventDocumentFactory.From(Scope, Change()), ReplicaDocumentSerialization.Options));

        var root = json.RootElement;

        Assert.Equal(EventId.ToString("D"), root.GetProperty("id").GetString());
        Assert.Equal(Scope.OwnerId.Value.ToString("D"), root.GetProperty("ownerId").GetString());
        Assert.Equal(Scope.DeviceId.Value.ToString("D"), root.GetProperty("deviceId").GetString());
        Assert.Equal("knowledge-event", root.GetProperty("kind").GetString());
        Assert.Equal("NDC Oslo 2026", root.GetProperty("document").GetProperty("title").GetString());
    }

    /// <summary>No tombstones and no TTL: an event is never deleted on the feed, so nothing
    /// here may expire.</summary>
    [Fact]
    public void A_document_carries_no_ttl()
    {
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(KnowledgeEventDocumentFactory.From(Scope, Change()), ReplicaDocumentSerialization.Options));

        Assert.False(json.RootElement.TryGetProperty("ttl", out _));
        Assert.False(json.RootElement.TryGetProperty("deletedAt", out _));
    }

    [Fact]
    public void The_default_names_what_the_app_model_creates()
    {
        Assert.Equal("knowledgeEvents", new CosmosOptions().KnowledgeEventsContainerName);
    }

    [Fact]
    public void A_document_that_is_not_one_of_ours_maps_to_nothing()
    {
        Assert.Null(KnowledgeEventDocumentFactory.ToRecord(new KnowledgeEventDocument { Id = "not-a-guid" }));

        Assert.Null(KnowledgeEventDocumentFactory.ToRecord(new KnowledgeEventDocument
        {
            Id = EventId.ToString("D"),
            DeviceId = Scope.DeviceId.Value.ToString("D"),
            Kind = KnowledgeEventKinds.KnowledgeEvent,
        }));

        Assert.Null(KnowledgeEventDocumentFactory.ToRecord(new KnowledgeEventDocument
        {
            Id = EventId.ToString("D"),
            DeviceId = Scope.DeviceId.Value.ToString("D"),
            Kind = "note",
            Document = Json(Event),
        }));
    }

    /// <summary>The module's <c>TryAdd</c> yields to the Cosmos adapter, as it does for every
    /// other replica; resolving it reaches no account, because the container is resolved
    /// lazily.</summary>
    [Fact]
    public void With_cosmos_configured_the_module_yields_its_knowledge_event_replica()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Production });
        builder.Configuration["Sync:Cosmos:AccountEndpoint"] = "https://localhost:8081/";

        builder.AddCosmosReplicas();
        builder.Services.AddSyncModule();

        using var provider = builder.Services.BuildServiceProvider();

        Assert.IsType<CosmosKnowledgeEventReplica>(provider.GetRequiredService<IKnowledgeEventReplica>());
    }

    private static KnowledgeEventChange Change() => new(EventId, KnowledgeEventKinds.KnowledgeEvent, Noon, Json(Event));

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
