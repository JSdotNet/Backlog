using System.Buffers.Text;
using System.Text;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Api.Options;
using Backlog.Modules.Sync.Api.Security;
using Backlog.Modules.Sync.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// The pull cursor on its own, away from HTTP. The endpoint tests prove the
/// codec is wired in; these prove what it decides, including the two failures a
/// client can tell apart only because the codec keeps them apart.
/// </summary>
public class SyncCursorCodecTests
{
    private static readonly OwnerId Mine = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly OwnerId Theirs = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    private static HmacSyncCursorCodec Codec(string? signingKey = null) =>
        new(Microsoft.Extensions.Options.Options.Create(new SyncTokenOptions
        {
            SigningKey = signingKey ?? SyncServiceFactory.SigningKey,
        }));

    [Fact]
    public void A_minted_cursor_comes_back_as_it_went_in()
    {
        var codec = Codec();

        var verified = codec.Verify(codec.Mint(Mine, "continuation-from-the-store"), Mine);

        Assert.True(verified.IsSuccess);
        Assert.Equal(Mine, verified.Value.Owner);
        Assert.Equal("continuation-from-the-store", verified.Value.Continuation);
    }

    /// <summary>A continuation is opaque and the store is free to put anything
    /// in it, including the JSON Cosmos actually returns. Base64url in the
    /// middle segment is what keeps that safe to put in a query string.</summary>
    [Fact]
    public void A_continuation_full_of_punctuation_survives_the_round_trip()
    {
        var codec = Codec();
        const string awkward = """{"V":2,"Rid":"abc==","Continuation":[{"token":"\"42\"","range":{"min":"","max":"FF"}}]}""";

        var verified = codec.Verify(codec.Mint(Mine, awkward), Mine);

        Assert.True(verified.IsSuccess);
        Assert.Equal(awkward, verified.Value.Continuation);
    }

    [Fact]
    public void A_cursor_that_is_not_ours_at_all_is_malformed()
    {
        var codec = Codec();

        foreach (var nonsense in new[] { string.Empty, "  ", "not-a-cursor", "v2.aaa.bbb", "v1.aaa", "v1.!!!.!!!" })
        {
            var verified = codec.Verify(nonsense, Mine);

            Assert.True(verified.IsFailure, nonsense);
            Assert.Equal(SyncErrorCodes.SyncCursorMalformed, verified.Error.Code);
            Assert.Equal(ErrorType.Validation, verified.Error.Type);
        }
    }

    /// <summary>The one a client could plausibly produce by accident and an
    /// attacker on purpose: a real cursor with a byte changed.</summary>
    [Fact]
    public void A_tampered_signature_is_malformed()
    {
        var codec = Codec();
        var minted = codec.Mint(Mine, "continuation");

        var verified = codec.Verify(Tamper(minted), Mine);

        Assert.True(verified.IsFailure);
        Assert.Equal(SyncErrorCodes.SyncCursorMalformed, verified.Error.Code);
    }

    /// <summary>A payload edited to name another owner does not verify either,
    /// because the signature covers the payload. This is the case that would
    /// otherwise let a caller promote itself into somebody else's feed without
    /// ever needing a cursor of theirs.</summary>
    [Fact]
    public void A_payload_rewritten_to_another_owner_is_malformed()
    {
        var codec = Codec();
        var parts = codec.Mint(Mine, "continuation").Split('.');
        var payload = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(parts[1]))
            .Replace(Mine.ToString(), Theirs.ToString(), StringComparison.Ordinal);

        var forged = $"{parts[0]}.{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload))}.{parts[2]}";

        var verified = codec.Verify(forged, Theirs);

        Assert.True(verified.IsFailure);
        Assert.Equal(SyncErrorCodes.SyncCursorMalformed, verified.Error.Code);
    }

    /// <summary>
    /// The case .arc42/adr/0005 §Consequences names: a cursor this service
    /// really did mint, replayed verbatim by a different owner. It verifies, and
    /// it is still refused — with its own code, because "somebody presented a
    /// valid cursor for another person's feed" is a different event from
    /// "somebody sent junk" and only one of them is worth waking up for.
    /// </summary>
    [Fact]
    public void A_cursor_minted_for_another_owner_is_refused_by_its_own_code()
    {
        var codec = Codec();

        var verified = codec.Verify(codec.Mint(Theirs, "continuation"), Mine);

        Assert.True(verified.IsFailure);
        Assert.Equal(SyncErrorCodes.SyncCursorNotYours, verified.Error.Code);
    }

    /// <summary>Two services with different keys mint cursors the other will not
    /// take. The cursor key is derived from the token signing key, so this also
    /// pins that the derivation actually uses it.</summary>
    [Fact]
    public void A_cursor_minted_under_another_key_does_not_verify()
    {
        var minted = Codec(SyncServiceFactory.OtherSigningKey).Mint(Mine, "continuation");

        var verified = Codec().Verify(minted, Mine);

        Assert.True(verified.IsFailure);
        Assert.Equal(SyncErrorCodes.SyncCursorMalformed, verified.Error.Code);
    }

    private static string Tamper(string cursor)
    {
        var parts = cursor.Split('.');
        var signature = Base64Url.DecodeFromChars(parts[2]);
        signature[0] ^= 0xFF;

        return $"{parts[0]}.{parts[1]}.{Base64Url.EncodeToString(signature)}";
    }
}
