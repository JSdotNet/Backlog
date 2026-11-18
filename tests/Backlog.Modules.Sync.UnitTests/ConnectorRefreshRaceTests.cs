using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.Connector;
using Backlog.Modules.Sync.Features.Enrolment;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// The refresh-token family against concurrent callers, and a refresh against a changed
/// configuration (local ADR 0052 §8). A concurrent caller is a wrapper around the store that
/// runs the other caller's step at the exact point the race would interleave it.
/// </summary>
public sealed class ConnectorRefreshRaceTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_code_reuse_that_revokes_before_the_first_exchange_stores_its_token_still_kills_it()
    {
        var store = new InterleavingStore();
        var connector = new ConnectorFixture(refreshTokens: store.Wrap);
        var secret = await connector.Secret();
        store.BeforeAdd = token => store.Inner.RevokeFamily(token.FamilyId, Cancellation);

        var first = (await connector.Exchanged(secret)).Value;
        var refresh = await connector.Refresh.Handle(Refreshing(secret, first.RefreshToken), Cancellation);

        Assert.Equal(OAuthErrors.InvalidGrant, refresh.Error.Code);
    }

    [Fact]
    public async Task A_replay_that_revokes_before_the_rotation_leaves_nothing_issued()
    {
        var store = new InterleavingStore();
        var connector = new ConnectorFixture(refreshTokens: store.Wrap);
        var secret = await connector.Secret();
        var first = (await connector.Exchanged(secret)).Value;
        store.BeforeRotate = replacement => store.Inner.RevokeFamily(replacement.FamilyId, Cancellation);

        var refresh = await connector.Refresh.Handle(Refreshing(secret, first.RefreshToken), Cancellation);

        Assert.Equal(OAuthErrors.InvalidGrant, refresh.Error.Code);
        Assert.Single(connector.Tokens.Issued);
    }

    [Fact]
    public async Task A_replay_that_revokes_right_after_the_rotation_kills_the_replacement()
    {
        var store = new InterleavingStore();
        var connector = new ConnectorFixture(refreshTokens: store.Wrap);
        var secret = await connector.Secret();
        var first = (await connector.Exchanged(secret)).Value;
        store.AfterRotate = replacement => store.Inner.RevokeFamily(replacement.FamilyId, Cancellation);

        await connector.Refresh.Handle(Refreshing(secret, first.RefreshToken), Cancellation);
        store.AfterRotate = null;
        var replacement = Assert.Single(store.Rotated);
        var found = await store.Inner.FindByHash(replacement.TokenHash, Cancellation);

        Assert.True(found!.IsSpent);
    }

    [Fact]
    public async Task A_caller_that_loses_the_rotation_mints_nothing_and_stamps_nothing()
    {
        var store = new InterleavingStore();
        var connector = new ConnectorFixture(refreshTokens: store.Wrap);
        var secret = await connector.Secret();
        var first = (await connector.Exchanged(secret)).Value;
        var stampedAt = connector.Clock.GetUtcNow();
        connector.Clock.Advance(TimeSpan.FromMinutes(5));
        store.LoseRotation = true;

        var refresh = await connector.Refresh.Handle(Refreshing(secret, first.RefreshToken), Cancellation);
        var client = await connector.Clients.Find(ConnectorSettings.DefaultClientId, Cancellation);

        Assert.Equal(OAuthErrors.InvalidGrant, refresh.Error.Code);
        Assert.Single(connector.Tokens.Issued);
        Assert.Equal(stampedAt, client!.LastUsedAt);
    }

    [Fact]
    public async Task A_refresh_token_of_an_owner_no_longer_configured_is_an_invalid_grant()
    {
        var connector = new ConnectorFixture();
        var secret = await connector.Secret();
        var first = (await connector.Exchanged(secret)).Value;
        var otherOwner = connector.RefreshUnder(new OwnerEnrolment(ConnectorFixture.OwnerGitHubId + 1), connector.Settings);

        var refresh = await otherOwner.Handle(Refreshing(secret, first.RefreshToken), Cancellation);

        Assert.Equal(OAuthErrors.InvalidGrant, refresh.Error.Code);
        Assert.Single(connector.Tokens.Issued);
    }

    [Fact]
    public async Task A_refresh_token_for_a_resource_no_longer_configured_is_an_invalid_grant()
    {
        var connector = new ConnectorFixture();
        var secret = await connector.Secret();
        var first = (await connector.Exchanged(secret)).Value;
        var movedResource = connector.RefreshUnder(connector.Owner, connector.Settings with { Resource = "https://moved.test/mcp" });

        var refresh = await movedResource.Handle(Refreshing(secret, first.RefreshToken), Cancellation);

        Assert.Equal(OAuthErrors.InvalidGrant, refresh.Error.Code);
        Assert.Single(connector.Tokens.Issued);
    }

    private static RefreshConnectorTokenCommand Refreshing(string secret, string refreshToken) =>
        new(ConnectorFixture.Credentials(secret), refreshToken, Scope: null);

    /// <summary>The fixture's store with a concurrent caller's step run at a chosen point.</summary>
    private sealed class InterleavingStore : IConnectorRefreshTokenStore
    {
        private readonly List<ConnectorRefreshToken> _rotated = [];

        internal IConnectorRefreshTokenStore Inner { get; private set; } = null!;

        internal Func<ConnectorRefreshToken, Task>? BeforeAdd { get; set; }

        internal Func<ConnectorRefreshToken, Task>? BeforeRotate { get; set; }

        internal Func<ConnectorRefreshToken, Task>? AfterRotate { get; set; }

        /// <summary>Answers every rotation as lost to another caller, storing nothing.</summary>
        internal bool LoseRotation { get; set; }

        internal IReadOnlyList<ConnectorRefreshToken> Rotated => _rotated;

        internal IConnectorRefreshTokenStore Wrap(IConnectorRefreshTokenStore inner)
        {
            Inner = inner;
            return this;
        }

        public async Task Add(ConnectorRefreshToken token, CancellationToken cancellationToken = default)
        {
            if (BeforeAdd is not null)
            {
                await BeforeAdd(token);
            }

            await Inner.Add(token, cancellationToken);
        }

        public Task<ConnectorRefreshToken?> FindByHash(string tokenHash, CancellationToken cancellationToken = default) =>
            Inner.FindByHash(tokenHash, cancellationToken);

        public async Task<bool> TryRotate(string presentedHash, ConnectorRefreshToken replacement, CancellationToken cancellationToken = default)
        {
            if (LoseRotation)
            {
                return false;
            }

            if (BeforeRotate is not null)
            {
                await BeforeRotate(replacement);
            }

            var rotated = await Inner.TryRotate(presentedHash, replacement, cancellationToken);
            if (rotated)
            {
                _rotated.Add(replacement);
            }

            if (AfterRotate is not null)
            {
                await AfterRotate(replacement);
            }

            return rotated;
        }

        public Task RevokeFamily(Guid familyId, CancellationToken cancellationToken = default) =>
            Inner.RevokeFamily(familyId, cancellationToken);
    }
}
