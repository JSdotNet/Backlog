// What is testable about the annotation adapter without Cosmos: the shape of a
// document, the names the container is partitioned on, and the TTL rule. What
// is not — change-feed delivery, TTL expiry — is not, for the reasons
// TaskDocumentTests gives at its head.

using System.Text.Json;
using Backlog.Infrastructure.Cosmos.Annotations;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

public class AnnotationDocumentTests
{
    private static readonly OwnerScope Scope = new(
        new OwnerId(Guid.Parse("2a1d1b8e-2f2c-4a4b-9c3d-5e6f70819293")),
        new DeviceId(Guid.Parse("3b2e2c9f-3a3d-4b5c-8d4e-6f708192a3b4")));

    private static readonly Guid AnnotationId = Guid.Parse("5d4a4ebb-5c5f-4d7e-af60-8192a3b4c5d6");

    [Fact]
    public void A_change_round_trips_through_a_document()
    {
        var change = Change();

        var record = AnnotationDocumentFactory.ToRecord(AnnotationDocumentFactory.From(Scope, change, new CosmosOptions()));

        Assert.NotNull(record);
        Assert.Equal(change.Id, record.Change.Id);
        Assert.Equal(change.UpdatedAt, record.Change.UpdatedAt);
        Assert.Null(record.Change.DeletedAt);
        Assert.Equal(Scope.DeviceId.Value, record.DeviceId);
        Assert.Equal(change.Annotation, record.Change.Annotation);
    }

    [Fact]
    public void The_document_serialises_with_the_names_the_container_is_partitioned_on()
    {
        var document = AnnotationDocumentFactory.From(Scope, Change(), new CosmosOptions());

        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(document, ReplicaDocumentSerialization.Options));

        var root = json.RootElement;

        Assert.Equal(AnnotationId.ToString("D"), root.GetProperty("id").GetString());
        Assert.Equal(Scope.OwnerId.Value.ToString("D"), root.GetProperty("ownerId").GetString());
        Assert.Equal(Scope.DeviceId.Value.ToString("D"), root.GetProperty("deviceId").GetString());

        var annotation = root.GetProperty("annotation");
        Assert.Equal("JSdotNet/Backlog", annotation.GetProperty("repositoryAlias").GetString());
        Assert.Equal(".domain/devbook/features.md", annotation.GetProperty("chapterPath").GetString());
        Assert.Equal(2, annotation.GetProperty("blockIndex").GetInt32());
    }

    [Fact]
    public void A_live_annotation_is_written_without_a_ttl()
    {
        var document = AnnotationDocumentFactory.From(Scope, Change(), new CosmosOptions());

        Assert.Null(document.Ttl);

        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(document, ReplicaDocumentSerialization.Options));

        Assert.False(json.RootElement.TryGetProperty("ttl", out _));
        Assert.False(json.RootElement.TryGetProperty("deletedAt", out _));
    }

    [Fact]
    public void A_tombstone_is_written_with_the_configured_ttl()
    {
        var options = new CosmosOptions { AnnotationTombstoneTtlSeconds = 4_321 };
        var deleted = Change() with { DeletedAt = DateTimeOffset.UtcNow };

        var document = AnnotationDocumentFactory.From(Scope, deleted, options);

        Assert.Equal(4_321, document.Ttl);
    }

    [Fact]
    public void The_defaults_name_what_the_app_model_creates_and_keep_the_task_retention()
    {
        var options = new CosmosOptions();

        Assert.Equal("annotations", options.AnnotationsContainerName);
        Assert.Equal(options.TaskTombstoneTtlSeconds, options.AnnotationTombstoneTtlSeconds);
    }

    [Fact]
    public void A_document_that_is_not_one_of_ours_maps_to_nothing()
    {
        Assert.Null(AnnotationDocumentFactory.ToRecord(new AnnotationDocument { Id = "not-a-guid" }));

        Assert.Null(AnnotationDocumentFactory.ToRecord(new AnnotationDocument
        {
            Id = AnnotationId.ToString("D"),
            DeviceId = Scope.DeviceId.Value.ToString("D"),
            Annotation = null,
        }));
    }

    private static AnnotationChange Change() => new(
        AnnotationId,
        new DateTimeOffset(2026, 9, 16, 10, 30, 0, TimeSpan.Zero),
        DeletedAt: null,
        new AnnotationPayload(
            "JSdotNet/Backlog",
            ".domain/devbook/features.md",
            2,
            "Say which team owns this.",
            "DEV-TOWER",
            new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero),
            Resolved: false));
}
