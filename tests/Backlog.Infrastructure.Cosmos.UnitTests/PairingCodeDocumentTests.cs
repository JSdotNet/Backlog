// What is testable about the pairing-code store without Cosmos: the shape of a
// document, the JSON contract the partition key depends on, and the TTL rule.
//
// Not tested here, deliberately. The burn — a conditional replace on the
// document's own etag — needs a store that issues etags and refuses stale ones,
// which the emulator does and a fake could only pretend to. And TTL expiry is
// not reachable at all: the emulator does not honour TTL, so reaping is
// deployed-only behaviour and the handler's own HasExpired check is what
// actually turns a stale code away. What is pinned below is that every code is
// written with a ttl and that the number is the right one.

using System.Text.Json;
using Backlog.Infrastructure.Cosmos.PairingCodes;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

public class PairingCodeDocumentTests
{
    private static readonly OwnerId Owner = new(Guid.Parse("2a1d1b8e-2f2c-4a4b-9c3d-5e6f70819293"));
    private static readonly DeviceId Issuer = new(Guid.Parse("3b2e2c9f-3a3d-4b5c-8d4e-6f708192a3b4"));
    private static readonly DateTimeOffset IssuedAt = new(2026, 9, 14, 8, 15, 0, TimeSpan.Zero);
    private static readonly string Hash = "cd" + new string('0', 62);

    [Fact]
    public void A_code_round_trips_through_a_document()
    {
        var code = Issued();

        var restored = PairingCodeDocumentFactory.ToPairingCode(PairingCodeDocumentFactory.From(code, IssuedAt));

        Assert.Equal(code, restored);
    }

    [Fact]
    public void A_redeemed_code_round_trips_as_redeemed()
    {
        var code = Issued() with { Redeemed = true };

        var restored = PairingCodeDocumentFactory.ToPairingCode(PairingCodeDocumentFactory.From(code, IssuedAt));

        Assert.NotNull(restored);
        Assert.True(restored.Redeemed);
    }

    /// <summary>
    /// The container is partitioned on <c>/id</c>, and the id is the code hash:
    /// it is the only handle a redemption has, so it is the whole address of the
    /// document. Nothing fails when the property is misspelt — the document
    /// lands in the undefined partition — so the text is asserted.
    /// </summary>
    [Fact]
    public void The_document_serialises_with_the_names_the_container_is_partitioned_on()
    {
        var document = PairingCodeDocumentFactory.From(Issued(), IssuedAt);

        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(document, ReplicaDocumentSerialization.Options));

        var root = json.RootElement;

        Assert.Equal(Hash, root.GetProperty("id").GetString());
        Assert.Equal(Owner.Value.ToString("D"), root.GetProperty("ownerId").GetString());
        Assert.Equal(Issuer.Value.ToString("D"), root.GetProperty("issuedBy").GetString());
        Assert.False(root.GetProperty("redeemed").GetBoolean());
        Assert.True(root.TryGetProperty("expiresAt", out _));
    }

    /// <summary>
    /// Every code carries a <c>ttl</c>: the window plus a grace, from the moment
    /// it is written. The grace is what lets the handler keep saying "expired"
    /// rather than "not found" for a while after the window closes — the
    /// friendlier answer for a person who typed a code a minute too late — and
    /// the container's <c>defaultTtl</c> of -1 is what makes the property count.
    /// </summary>
    [Fact]
    public void A_code_is_written_with_a_ttl_covering_its_window_and_the_grace()
    {
        var document = PairingCodeDocumentFactory.From(Issued(), IssuedAt);

        Assert.Equal(600 + PairingCodeDocumentFactory.ReapGraceSeconds, document.Ttl);

        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(document, ReplicaDocumentSerialization.Options));

        Assert.Equal(document.Ttl, json.RootElement.GetProperty("ttl").GetInt32());
    }

    /// <summary>A code written after its own expiry — a clock that jumped, a
    /// test with a fixed now — still gets a positive ttl. Cosmos rejects a ttl
    /// of zero or below as "delete on write" for nobody's benefit.</summary>
    [Fact]
    public void A_code_already_past_its_window_still_gets_the_grace_as_its_ttl()
    {
        var document = PairingCodeDocumentFactory.From(Issued(), IssuedAt.AddHours(1));

        Assert.Equal(PairingCodeDocumentFactory.ReapGraceSeconds, document.Ttl);
    }

    [Fact]
    public void A_document_that_is_not_one_of_ours_maps_to_nothing()
    {
        Assert.Null(PairingCodeDocumentFactory.ToPairingCode(new PairingCodeDocument
        {
            Id = Hash,
            OwnerId = "not-a-guid",
            IssuedBy = Issuer.Value.ToString("D"),
        }));

        Assert.Null(PairingCodeDocumentFactory.ToPairingCode(new PairingCodeDocument
        {
            Id = Hash,
            OwnerId = Owner.Value.ToString("D"),
            IssuedBy = "not-a-guid",
        }));
    }

    private static PairingCode Issued() => new(
        Hash,
        Owner,
        Issuer,
        IssuedAt.AddMinutes(10));
}
