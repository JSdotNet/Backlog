// What is testable about the connector credential documents without Cosmos: the shape of
// the three documents, the ids every point operation addresses (the container is never
// queried), the TTL rule, and the two decisions each compare-and-swap is built on —
// whether a stamp still applies to the stored generation, and whether a refresh token may
// still be rotated out. What the stores do when Cosmos faults is
// ConnectorCredentialStoreFaultTests; the etag semantics themselves are the emulator's.

using System.Text.Json;
using Backlog.Infrastructure.Cosmos.ConnectorCredentials;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

public class ConnectorCredentialDocumentTests
{
    private static readonly OwnerId Owner = new(Guid.Parse("2a1d1b8e-2f2c-4a4b-9c3d-5e6f70819293"));
    private static readonly Guid Family = Guid.Parse("4c3f3d0a-4b4e-4c6d-9e5f-708192a3b4c5");
    private static readonly DateTimeOffset IssuedAt = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly string SecretHash = "ab" + new string('0', 62);
    private static readonly string TokenHash = "cd" + new string('0', 62);
    private static readonly string NextHash = "ef" + new string('0', 62);

    [Fact]
    public void A_client_round_trips_through_a_document()
    {
        var client = new ConnectorClient("claude-connector", SecretHash, IssuedAt, IssuedAt.AddHours(1), 3);

        var restored = ConnectorCredentialDocumentFactory.ToClient(ConnectorCredentialDocumentFactory.From(client));

        Assert.Equal(client, restored);
    }

    [Fact]
    public void A_revoked_client_round_trips_without_a_secret()
    {
        var client = new ConnectorClient("claude-connector", null, null, null, 4);

        var restored = ConnectorCredentialDocumentFactory.ToClient(ConnectorCredentialDocumentFactory.From(client));

        Assert.Equal(client, restored);
        Assert.False(restored.HasSecret);
    }

    /// <summary>
    /// The container is partitioned on <c>/id</c> and holds three kinds of document, so the
    /// id carries the kind: the client's is <c>client:&lt;client id&gt;</c>. It never
    /// expires — a lost hash disconnects the connector — so it carries no <c>ttl</c>, and
    /// nulls are dropped rather than written.
    /// </summary>
    [Fact]
    public void The_client_document_is_addressed_by_its_client_id_and_never_expires()
    {
        var document = ConnectorCredentialDocumentFactory.From(new ConnectorClient("claude-connector", null, null, null, 2));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(document, ReplicaDocumentSerialization.Options));
        var root = json.RootElement;

        Assert.Equal("client:claude-connector", root.GetProperty("id").GetString());
        Assert.Equal("client", root.GetProperty("kind").GetString());
        Assert.Equal("claude-connector", root.GetProperty("clientId").GetString());
        Assert.Equal(2, root.GetProperty("generation").GetInt32());
        Assert.False(root.TryGetProperty("ttl", out _));
        Assert.False(root.TryGetProperty("secretHash", out _));
    }

    [Fact]
    public void A_stamp_for_the_current_generation_records_the_last_use()
    {
        var document = ConnectorCredentialDocumentFactory.From(new ConnectorClient("claude-connector", SecretHash, IssuedAt, null, 3));

        Assert.True(ConnectorCredentialDocumentFactory.Stamp(document, 3, IssuedAt.AddMinutes(5)));

        Assert.Equal(IssuedAt.AddMinutes(5), ConnectorCredentialDocumentFactory.ToClient(document).LastUsedAt);
    }

    /// <summary>A stamp that raced a revoke or a new secret carries the old generation, and
    /// must leave the document as it is, so the replace that would have written it is never
    /// sent.</summary>
    [Fact]
    public void A_stamp_for_an_older_generation_changes_nothing()
    {
        var client = new ConnectorClient("claude-connector", null, null, null, 4);
        var document = ConnectorCredentialDocumentFactory.From(client);

        Assert.False(ConnectorCredentialDocumentFactory.Stamp(document, 3, IssuedAt.AddMinutes(5)));

        Assert.Equal(client, ConnectorCredentialDocumentFactory.ToClient(document));
    }

    [Fact]
    public void A_refresh_token_round_trips_through_a_document()
    {
        var token = Issued();

        var restored = ConnectorCredentialDocumentFactory.ToRefreshToken(ConnectorCredentialDocumentFactory.From(token, IssuedAt));

        Assert.Equal(token, restored);
    }

    [Fact]
    public void A_rotated_and_revoked_refresh_token_round_trips_as_spent()
    {
        var token = Issued() with { ReplacedByHash = NextHash, Revoked = true };

        var restored = ConnectorCredentialDocumentFactory.ToRefreshToken(ConnectorCredentialDocumentFactory.From(token, IssuedAt));

        Assert.Equal(token, restored);
        Assert.True(restored!.IsSpent);
    }

    /// <summary>
    /// A refresh token is addressed by its hash — the only handle a refresh has — under the
    /// <c>refresh:</c> prefix, and carries the <c>familyId</c> the store turns into the
    /// address of its family's revocation marker. Nothing fails when one of them is
    /// misspelt, so the text is asserted.
    /// </summary>
    [Fact]
    public void The_refresh_token_document_serialises_with_the_names_the_store_reads()
    {
        var document = ConnectorCredentialDocumentFactory.From(Issued(), IssuedAt);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(document, ReplicaDocumentSerialization.Options));
        var root = json.RootElement;

        Assert.Equal("refresh:" + TokenHash, root.GetProperty("id").GetString());
        Assert.Equal("refreshToken", root.GetProperty("kind").GetString());
        Assert.Equal(Family.ToString("D"), root.GetProperty("familyId").GetString());
        Assert.Equal(Owner.Value.ToString("D"), root.GetProperty("ownerId").GetString());
        Assert.Equal(TokenHash, root.GetProperty("tokenHash").GetString());
        Assert.False(root.GetProperty("revoked").GetBoolean());
        Assert.False(root.TryGetProperty("replacedByHash", out _));
    }

    /// <summary>Every refresh token carries a <c>ttl</c>: its thirty days plus a grace, from
    /// the moment it is written, so a replay a little after expiry is still recognised and
    /// Cosmos reaps the rest.</summary>
    [Fact]
    public void A_refresh_token_is_written_with_a_ttl_covering_its_life_and_the_grace()
    {
        var document = ConnectorCredentialDocumentFactory.From(Issued(), IssuedAt);

        Assert.Equal((30 * 86_400) + ConnectorCredentialDocumentFactory.ReapGraceSeconds, document.Ttl);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(document, ReplicaDocumentSerialization.Options));
        Assert.Equal(document.Ttl, json.RootElement.GetProperty("ttl").GetInt32());
    }

    /// <summary>A write after the token's own expiry still gets a positive ttl: Cosmos reads
    /// zero or below as "delete on write".</summary>
    [Fact]
    public void A_refresh_token_already_past_its_life_still_gets_the_grace_as_its_ttl()
    {
        Assert.Equal(
            ConnectorCredentialDocumentFactory.ReapGraceSeconds,
            ConnectorCredentialDocumentFactory.TtlFor(IssuedAt.AddDays(30), IssuedAt.AddDays(31)));
    }

    [Fact]
    public void A_live_refresh_token_rotates_to_its_replacement_with_a_ttl_from_now()
    {
        var document = ConnectorCredentialDocumentFactory.From(Issued(), IssuedAt);
        var now = IssuedAt.AddDays(10);

        Assert.True(ConnectorCredentialDocumentFactory.Rotate(document, NextHash, now));

        Assert.Equal(NextHash, document.ReplacedByHash);
        Assert.Equal((20 * 86_400) + ConnectorCredentialDocumentFactory.ReapGraceSeconds, document.Ttl);
    }

    /// <summary>The compare half of the swap: a token already rotated out or revoked is a
    /// replay, so the replace is never sent and the caller loses.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_spent_refresh_token_does_not_rotate(bool rotated, bool revoked)
    {
        var token = Issued() with { ReplacedByHash = rotated ? NextHash : null, Revoked = revoked };
        var document = ConnectorCredentialDocumentFactory.From(token, IssuedAt);

        Assert.False(ConnectorCredentialDocumentFactory.Rotate(document, "ff" + new string('0', 62), IssuedAt));

        Assert.Equal(token, ConnectorCredentialDocumentFactory.ToRefreshToken(document));
    }

    /// <summary>
    /// A family is revoked by one marker document, <c>family:&lt;family id&gt;</c>, written
    /// before anything else, rather than by a write per token that a concurrent rotation
    /// could slip a fresh token past. Its ttl outlives the longest refresh token any family
    /// can hold — ninety days, the top of <c>RefreshTokenDays</c> — plus the grace.
    /// </summary>
    [Fact]
    public void A_family_revocation_is_one_marker_that_outlives_every_token_of_the_family()
    {
        var marker = ConnectorCredentialDocumentFactory.FamilyRevocation(Family, IssuedAt);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(marker, ReplicaDocumentSerialization.Options));
        var root = json.RootElement;

        Assert.Equal("family:" + Family.ToString("D"), root.GetProperty("id").GetString());
        Assert.Equal(ConnectorCredentialDocumentFactory.FamilyRevocationId(Family), root.GetProperty("id").GetString());
        Assert.Equal("familyRevocation", root.GetProperty("kind").GetString());
        Assert.Equal(Family.ToString("D"), root.GetProperty("familyId").GetString());
        Assert.True(root.TryGetProperty("revokedAt", out _));

        var ttl = root.GetProperty("ttl").GetInt32();
        var longestToken = ConnectorCredentialDocumentFactory.TtlFor(IssuedAt.AddDays(ConnectorTokenLimits.MaxRefreshTokenDays), IssuedAt);
        Assert.True(ttl > longestToken, $"marker ttl {ttl} must outlive the longest token's {longestToken}");
    }

    /// <summary>A token whose family carries a marker reads as revoked whatever its own
    /// document says — the marker is the truth, so a token written after the revoke, or one
    /// whose own flag was never set, cannot be presented as live.</summary>
    [Fact]
    public void A_token_of_a_revoked_family_reads_as_revoked()
    {
        var document = ConnectorCredentialDocumentFactory.From(Issued(), IssuedAt);

        var restored = ConnectorCredentialDocumentFactory.ToRefreshToken(document, familyRevoked: true);

        Assert.NotNull(restored);
        Assert.True(restored.Revoked);
        Assert.True(restored.IsSpent);
        Assert.False(ConnectorCredentialDocumentFactory.ToRefreshToken(document, familyRevoked: false)!.Revoked);
    }

    [Fact]
    public void A_refresh_token_document_that_is_not_one_of_ours_maps_to_nothing()
    {
        var foreignOwner = ConnectorCredentialDocumentFactory.From(Issued(), IssuedAt);
        foreignOwner.OwnerId = "not-a-guid";

        var foreignFamily = ConnectorCredentialDocumentFactory.From(Issued(), IssuedAt);
        foreignFamily.FamilyId = "not-a-guid";

        Assert.Null(ConnectorCredentialDocumentFactory.ToRefreshToken(foreignOwner));
        Assert.Null(ConnectorCredentialDocumentFactory.ToRefreshToken(foreignFamily));
    }

    private static ConnectorRefreshToken Issued() => new(
        TokenHash,
        Family,
        ClientGeneration: 3,
        Owner,
        Scope: "inbox:capture",
        Resource: "https://sync.example/mcp",
        IssuedAt,
        IssuedAt.AddDays(30));
}
