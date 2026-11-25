using System.Text.Json;

using Backlog.Infrastructure.Sync.KnowledgeEvents;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The knowledge event's place on the wire (local ADR 0061): the address, its lookup and its
/// point ride the document as additive fields, so a desktop reads back what another sent, and a
/// document from an older desktop build, which knows none of them, reads as the venue
/// it holds.
/// </summary>
public sealed class KnowledgeEventDocumentsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly EventAddressReplicaDto Jaarbeurs = new("Jaarbeursplein 6", "3521 AL", "Utrecht", "Netherlands");

    private static KnowledgeEventReplicaDto Placed() =>
        KnowledgeEventCopies.Event("Techorama 2026", Noon) with
        {
            Venue = "Jaarbeurs",
            Address = Jaarbeurs,
            Lookup = AddressLookup.Placed,
            Point = new EventMapPointReplicaDto(52.08842, 5.11553),
        };

    [Fact]
    public void A_placed_address_round_trips_with_its_lookup_and_point()
    {
        var sent = Placed();

        var read = KnowledgeEventDocuments.ReadEvent(KnowledgeEventDocuments.ToChange(sent));

        Assert.NotNull(read);
        Assert.Equal("Jaarbeurs", read.Venue);
        Assert.Equal(Jaarbeurs, read.Address);
        Assert.Equal(AddressLookup.Placed, read.Lookup);
        Assert.Equal(new EventMapPointReplicaDto(52.08842, 5.11553), read.Point);
    }

    [Fact]
    public void The_place_goes_out_under_its_wire_names()
    {
        var document = KnowledgeEventDocuments.ToChange(Placed()).Document;

        var address = document.GetProperty("address");
        Assert.Equal("Jaarbeursplein 6", address.GetProperty("street").GetString());
        Assert.Equal("3521 AL", address.GetProperty("postcode").GetString());
        Assert.Equal("Utrecht", address.GetProperty("town").GetString());
        Assert.Equal("Netherlands", address.GetProperty("country").GetString());
        Assert.Equal("placed", document.GetProperty("lookup").GetString());
        Assert.Equal(52.08842, document.GetProperty("point").GetProperty("latitude").GetDouble());
        Assert.Equal(5.11553, document.GetProperty("point").GetProperty("longitude").GetDouble());
    }

    [Theory]
    [InlineData(AddressLookup.NotFound, "not-found")]
    [InlineData(AddressLookup.NotLookedUpYet, "not-looked-up-yet")]
    public void An_address_with_no_point_round_trips_and_leaves_the_point_out(AddressLookup lookup, string token)
    {
        var sent = KnowledgeEventCopies.Event("A meetup", Noon) with
        {
            Address = new EventAddressReplicaDto(null, null, "Utrecht", null),
            Lookup = lookup,
        };

        var change = KnowledgeEventDocuments.ToChange(sent);
        var read = KnowledgeEventDocuments.ReadEvent(change);

        Assert.Equal(token, change.Document.GetProperty("lookup").GetString());
        Assert.False(change.Document.TryGetProperty("point", out _));
        Assert.False(change.Document.GetProperty("address").TryGetProperty("street", out _));
        Assert.Equal(new EventAddressReplicaDto(null, null, "Utrecht", null), read!.Address);
        Assert.Equal(lookup, read.Lookup);
        Assert.Null(read.Point);
    }

    /// <summary>A venue alone sends none of the new fields, so the document is the one an older
    /// build wrote.</summary>
    [Fact]
    public void A_venue_alone_sends_no_address_lookup_or_point()
    {
        var document = KnowledgeEventDocuments.ToChange(KnowledgeEventCopies.Techorama(Noon, Guid.NewGuid())).Document;

        Assert.False(document.TryGetProperty("address", out _));
        Assert.False(document.TryGetProperty("lookup", out _));
        Assert.False(document.TryGetProperty("point", out _));
    }

    /// <summary>A document from an older desktop build, which knows none of the place fields, holds
    /// the venue and nothing more.</summary>
    [Fact]
    public void A_document_without_the_place_fields_reads_as_its_venue()
    {
        var id = Guid.NewGuid();
        var change = Change(id, $$"""{"id":"{{id:D}}","title":"Techorama 2025","state":"going","venue":"Kinepolis Antwerp","online":false}""");

        var read = KnowledgeEventDocuments.ReadEvent(change);

        Assert.NotNull(read);
        Assert.Equal("Kinepolis Antwerp", read.Venue);
        Assert.Null(read.Address);
        Assert.Null(read.Lookup);
        Assert.Null(read.Point);
    }

    /// <summary>A point that leaves a degree out reads with that degree absent, never as zero,
    /// so the replication refuses the copy rather than pinning it on the equator or Null Island.</summary>
    [Theory]
    [InlineData("""{"latitude":52.09}""", 52.09, null)]
    [InlineData("""{"longitude":5.12}""", null, 5.12)]
    [InlineData("""{}""", null, null)]
    public void A_point_missing_a_degree_reads_with_that_degree_absent(string point, double? latitude, double? longitude)
    {
        var id = Guid.NewGuid();
        var change = Change(id, $$"""{"id":"{{id:D}}","title":"Techorama 2025","state":"going","address":{"town":"Utrecht"},"lookup":"placed","point":{{point}}}""");

        var read = KnowledgeEventDocuments.ReadEvent(change);

        Assert.Equal(latitude, read!.Point!.Latitude);
        Assert.Equal(longitude, read.Point.Longitude);
    }

    [Fact]
    public void A_lookup_outside_its_set_does_not_read()
    {
        var id = Guid.NewGuid();
        var change = Change(id, $$"""{"id":"{{id:D}}","title":"Techorama 2025","state":"going","address":{"town":"Utrecht"},"lookup":"geocoded"}""");

        Assert.Null(KnowledgeEventDocuments.ReadEvent(change));
    }

    private static KnowledgeEventChange Change(Guid id, string json) =>
        new(id, KnowledgeEventKinds.KnowledgeEvent, Noon, JsonElement.Parse(json));
}
