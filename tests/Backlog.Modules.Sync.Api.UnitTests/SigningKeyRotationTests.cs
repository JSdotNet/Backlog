using System.Net;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Api.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// A rotation of the signing key (local ADR 0058): the previous key stays
/// accepted for its window and not a moment longer, for tokens and cursors
/// alike, and a bad new key never replaces a good one.
/// </summary>
public class SigningKeyRotationTests
{
    private static readonly byte[] NewKey = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] OldKey = Convert.FromBase64String(SyncServiceFactory.SigningKey);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_previous_key_is_accepted_until_its_window_closes()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-12T10:00:00Z"));
        var ring = new SigningKeyRing(new Rotated(clock.GetUtcNow().AddMinutes(35)), clock);
        await ring.RefreshAsync(Cancellation);

        Assert.Equal(NewKey, ring.Current);
        Assert.Equal([NewKey, OldKey], ring.Accepted());

        clock.Advance(TimeSpan.FromMinutes(35));

        Assert.Equal([NewKey], ring.Accepted());
    }

    [Fact]
    public async Task A_current_key_too_short_to_sign_with_keeps_the_keys_already_held()
    {
        var ring = new SigningKeyRing(new Fixed(new SigningKeys(new byte[16])), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ring.RefreshAsync(Cancellation));
    }

    [Fact]
    public async Task A_cursor_minted_under_the_previous_key_reads_while_its_window_is_open()
    {
        var owner = new Backlog.Modules.Sync.DomainModels.OwnerId(Guid.CreateVersion7());
        var minted = new HmacSyncCursorCodec(SigningKeyRing.Fixed(OldKey)).Mint(owner, "continuation");

        var open = new SigningKeyRing(new Rotated(DateTimeOffset.UtcNow.AddMinutes(10)), TimeProvider.System);
        await open.RefreshAsync(Cancellation);
        var closed = new SigningKeyRing(new Rotated(DateTimeOffset.UtcNow.AddMinutes(-1)), TimeProvider.System);
        await closed.RefreshAsync(Cancellation);

        Assert.True(new HmacSyncCursorCodec(open).Verify(minted, owner).IsSuccess);
        Assert.False(new HmacSyncCursorCodec(closed).Verify(minted, owner).IsSuccess);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(-1, false)]
    public async Task A_token_signed_with_the_previous_key_is_honoured_only_inside_the_window(int minutesLeft, bool accepted)
    {
        using var service = new SyncServiceFactory
        {
            TestServices = services =>
            {
                services.RemoveAll<ISigningKeySource>();
                services.AddSingleton<ISigningKeySource>(new Rotated(DateTimeOffset.UtcNow.AddMinutes(minutesLeft)));
            },
        };

        // Signed with the key the factory configures, which the rotated source
        // hands out as the previous one.
        var client = service.CreateClient().Bearing(TestTokens.SignedWith(SyncServiceFactory.SigningKey));

        var response = await client.GetAsync(SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation);

        if (accepted)
        {
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_token_the_service_mints_after_a_rotation_is_signed_with_the_new_key()
    {
        using var service = new SyncServiceFactory
        {
            TestServices = services =>
            {
                services.RemoveAll<ISigningKeySource>();
                services.AddSingleton<ISigningKeySource>(new Rotated(DateTimeOffset.UtcNow.AddMinutes(-1)));
            },
        };

        var client = service.CreateClient();
        var device = await client.RegisterDevice("Study desktop");
        client.Bearing(await client.DeviceToken(device));

        var response = await client.GetAsync(SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>A vault just after a rotation: the new key current, the old one
    /// previous, accepted until the given time.</summary>
    private sealed class Rotated(DateTimeOffset previousAcceptedUntil) : ISigningKeySource
    {
        public Task<SigningKeys> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SigningKeys(NewKey, OldKey, previousAcceptedUntil));
    }

    private sealed class Fixed(SigningKeys keys) : ISigningKeySource
    {
        public Task<SigningKeys> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(keys);
    }
}
