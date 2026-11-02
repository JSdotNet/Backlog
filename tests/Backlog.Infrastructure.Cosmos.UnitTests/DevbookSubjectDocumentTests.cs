// What is testable about the devbook subject adapter without Cosmos: the shape
// of a document, the names the container is partitioned on, that nothing about
// it expires, and that the host composes it. Change-feed delivery and the
// etag compare-and-write need an emulator, for the reasons TaskDocumentTests
// gives at its head.

using System.Text.Json;
using Backlog.Infrastructure.Cosmos.DevbookSubjects;
using Backlog.Infrastructure.Cosmos.Extensions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Extensions;
using Backlog.Modules.Sync.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

public class DevbookSubjectDocumentTests
{
    private static readonly OwnerScope Scope = new(
        new OwnerId(Guid.Parse("2a1d1b8e-2f2c-4a4b-9c3d-5e6f70819293")),
        new DeviceId(Guid.Parse("3b2e2c9f-3a3d-4b5c-8d4e-6f708192a3b4")));

    private static readonly Guid SubjectId = Guid.Parse("6e5b5fcc-6d60-5e8f-b071-92a3b4c5d6e7");
    private static readonly DateTimeOffset Noon = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_change_round_trips_through_a_document()
    {
        var change = Change();

        var record = DevbookSubjectDocumentFactory.ToRecord(DevbookSubjectDocumentFactory.From(Scope, change));

        Assert.NotNull(record);
        Assert.Equal(change, record.Change);
        Assert.Equal(Scope.DeviceId.Value, record.DeviceId);
    }

    [Fact]
    public void The_document_serialises_with_the_names_the_container_is_partitioned_on()
    {
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(DevbookSubjectDocumentFactory.From(Scope, Change()), ReplicaDocumentSerialization.Options));

        var root = json.RootElement;

        Assert.Equal(SubjectId.ToString("D"), root.GetProperty("id").GetString());
        Assert.Equal(Scope.OwnerId.Value.ToString("D"), root.GetProperty("ownerId").GetString());
        Assert.Equal(Scope.DeviceId.Value.ToString("D"), root.GetProperty("deviceId").GetString());

        var subject = root.GetProperty("subject");
        Assert.Equal("proposal", subject.GetProperty("kind").GetString());
        Assert.Equal("add-subject-store", subject.GetProperty("key").GetString());
        Assert.Equal("sha256:0123abcd", subject.GetProperty("approval").GetProperty("fingerprint").GetString());
    }

    /// <summary>No tombstones and no TTL (the subject container's terms): a
    /// state is set and never deleted, so nothing here may expire.</summary>
    [Fact]
    public void A_document_carries_no_ttl()
    {
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(DevbookSubjectDocumentFactory.From(Scope, Change()), ReplicaDocumentSerialization.Options));

        Assert.False(json.RootElement.TryGetProperty("ttl", out _));
        Assert.False(json.RootElement.TryGetProperty("deletedAt", out _));
    }

    [Fact]
    public void The_default_names_what_the_app_model_creates()
    {
        Assert.Equal("devbookSubjects", new CosmosOptions().DevbookSubjectsContainerName);
    }

    [Fact]
    public void A_document_that_is_not_one_of_ours_maps_to_nothing()
    {
        Assert.Null(DevbookSubjectDocumentFactory.ToRecord(new DevbookSubjectDocument { Id = "not-a-guid" }));

        Assert.Null(DevbookSubjectDocumentFactory.ToRecord(new DevbookSubjectDocument
        {
            Id = SubjectId.ToString("D"),
            DeviceId = Scope.DeviceId.Value.ToString("D"),
            Subject = null,
        }));
    }

    /// <summary>The module's <c>TryAdd</c> yields to the Cosmos adapter, as it
    /// does for every other replica; resolving it reaches no account, because the
    /// container is resolved lazily.</summary>
    [Fact]
    public void With_cosmos_configured_the_module_yields_its_subject_replica()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Production });
        builder.Configuration["Sync:Cosmos:AccountEndpoint"] = "https://localhost:8081/";

        builder.AddCosmosReplicas();
        builder.Services.AddSyncModule();

        using var provider = builder.Services.BuildServiceProvider();

        Assert.IsType<CosmosDevbookSubjectReplica>(provider.GetRequiredService<IDevbookSubjectReplica>());
    }

    private static DevbookSubjectChange Change() => new(
        SubjectId,
        Noon,
        new DevbookSubjectPayload(
            "JSdotNet/Backlog",
            "proposal",
            "add-subject-store",
            PullRequest: null,
            "approved",
            "DEV-TOWER",
            Noon,
            new DevbookSubjectDecisionPayload("DEV-TOWER", Noon, "sha256:0123abcd")));
}
