// What is testable about the device registry without Cosmos: the shape of a
// document, the JSON contract the container's partition key depends on, and the
// mapping back to the domain record the handlers read.
//
// Not tested here, deliberately: the point read, the owner count, and the
// cross-partition query behind it need a store — the emulator can give one, a
// fake would only assert that the fake was written to match the code.

using System.Text.Json;
using Backlog.Infrastructure.Cosmos.Devices;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

public class DeviceDocumentTests
{
    private static readonly OwnerId Owner = new(Guid.Parse("2a1d1b8e-2f2c-4a4b-9c3d-5e6f70819293"));
    private static readonly DeviceId Id = new(Guid.Parse("3b2e2c9f-3a3d-4b5c-8d4e-6f708192a3b4"));

    [Fact]
    public void A_device_round_trips_through_a_document()
    {
        var device = Registered();

        var restored = DeviceDocumentFactory.ToDevice(DeviceDocumentFactory.From(device));

        Assert.Equal(device, restored);
    }

    /// <summary>
    /// The container is partitioned on <c>/id</c> — the device id, because the
    /// one lookup on every token mint has only that in hand — and the owner is
    /// an ordinary indexed property the two cold queries filter on. Both names
    /// are asserted as text: nothing in the AppHost or the bicep fails when a
    /// document arrives without them, it lands in the undefined partition or
    /// falls out of the owner count instead.
    /// </summary>
    [Fact]
    public void The_document_serialises_with_the_names_the_container_and_the_queries_use()
    {
        var document = DeviceDocumentFactory.From(Registered());

        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(document, ReplicaDocumentSerialization.Options));

        var root = json.RootElement;

        Assert.Equal(Id.Value.ToString("D"), root.GetProperty("id").GetString());
        Assert.Equal(Owner.Value.ToString("D"), root.GetProperty("ownerId").GetString());
        Assert.Equal("Study laptop", root.GetProperty("name").GetString());
        Assert.Equal("ab" + new string('0', 62), root.GetProperty("credentialHash").GetString());
        Assert.True(root.TryGetProperty("registeredAt", out _));
    }

    /// <summary>A registration is for good: no <c>ttl</c>, not even a null one,
    /// and the container declares no default either.</summary>
    [Fact]
    public void A_device_is_written_without_a_ttl()
    {
        var document = DeviceDocumentFactory.From(Registered());

        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(document, ReplicaDocumentSerialization.Options));

        Assert.False(json.RootElement.TryGetProperty("ttl", out _));
    }

    /// <summary>The document goes through the same options as every other
    /// container's, so the names above are what actually land — reading the
    /// text back through them is the proof.</summary>
    [Fact]
    public void A_document_deserialises_through_the_shared_options()
    {
        var text = JsonSerializer.Serialize(
            DeviceDocumentFactory.From(Registered()), ReplicaDocumentSerialization.Options);

        var document = JsonSerializer.Deserialize<DeviceDocument>(text, ReplicaDocumentSerialization.Options);

        Assert.NotNull(document);
        Assert.Equal(Registered(), DeviceDocumentFactory.ToDevice(document));
    }

    /// <summary>A document whose id or owner is not a GUID is not one this
    /// service wrote. It maps to nothing, and the registry answers "no such
    /// device" — which on the token path is a 401 rather than a 500.</summary>
    [Fact]
    public void A_document_that_is_not_one_of_ours_maps_to_nothing()
    {
        Assert.Null(DeviceDocumentFactory.ToDevice(new DeviceDocument
        {
            Id = "not-a-guid",
            OwnerId = Owner.Value.ToString("D"),
        }));

        Assert.Null(DeviceDocumentFactory.ToDevice(new DeviceDocument
        {
            Id = Id.Value.ToString("D"),
            OwnerId = "not-a-guid",
        }));
    }

    [Fact]
    public void The_defaults_name_what_the_app_model_creates()
    {
        var options = new CosmosOptions();

        Assert.Equal("devices", options.DevicesContainerName);
        Assert.Equal("pairingCodes", options.PairingCodesContainerName);
    }

    private static Device Registered() => new(
        Id,
        Owner,
        "Study laptop",
        "ab" + new string('0', 62),
        new DateTimeOffset(2026, 9, 14, 8, 15, 0, TimeSpan.Zero));
}
