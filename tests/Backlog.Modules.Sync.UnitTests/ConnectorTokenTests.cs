using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Features.Connector;
using Backlog.Modules.Sync.Features.Enrolment;
using Backlog.SharedKernel.Mcp;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// The connector's token endpoint and its secret (local ADR 0058 §8): the secret is the
/// owner's to make and revoke and is kept only as a hash; a code works once and only with its
/// verifier; a refresh token rotates, and a replay or a revoke kills the whole family.
/// </summary>
public sealed class ConnectorTokenTests
{
    private readonly ConnectorFixture _connector = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_code_with_its_verifier_buys_a_token_for_the_owner_and_the_mcp_resource()
    {
        var secret = await _connector.Secret();

        var tokens = Assert.IsType<Ok<ConnectorTokenResponse>>((await _connector.Exchanged(secret)).Value).Value;

        Assert.Equal("Bearer", tokens.TokenType);
        Assert.Equal(900, tokens.ExpiresIn);
        Assert.Equal(McpScopes.InboxCapture, tokens.Scope);
        Assert.False(string.IsNullOrEmpty(tokens.RefreshToken));
        var claims = Assert.Single(_connector.Tokens.Issued);
        Assert.Equal(_connector.Owner.Owner, claims.Owner);
        Assert.Equal(ConnectorFixture.Resource, claims.Resource);
        Assert.Equal(ConnectorSettings.DefaultClientId, claims.ClientId);
    }

    [Fact]
    public async Task Using_the_secret_stamps_when_it_was_last_used()
    {
        var secret = await _connector.Secret();
        _connector.Clock.Advance(TimeSpan.FromHours(1));

        await _connector.Exchanged(secret);

        var status = Assert.IsType<Ok<ConnectorStatusResponse>>((await _connector.Describe.Handle(new DescribeConnectorQuery(_connector.OwnerDevice), Cancellation)).Value).Value;
        Assert.Equal(_connector.Clock.GetUtcNow(), status.LastUsedAt);
    }

    [Theory]
    [InlineData("not-the-secret")]
    [InlineData(null)]
    public async Task A_wrong_or_missing_secret_is_an_invalid_client(string? presented)
    {
        await _connector.Secret();

        var tokens = Assert.IsType<Error>((await _connector.Exchanged(presented!)).Value);

        Assert.Equal(OAuthErrors.InvalidClient, tokens.Code);
    }

    [Fact]
    public async Task A_wrong_verifier_is_an_invalid_grant()
    {
        var secret = await _connector.Secret();
        var code = await _connector.AuthorizationCode(EnrolmentPkce.CreateVerifier());

        var tokens = Assert.IsType<Error>((await _connector.Exchange.Handle(
            new ExchangeAuthorizationCodeCommand(ConnectorFixture.Credentials(secret), code, ConnectorSettings.DefaultRedirectUri, EnrolmentPkce.CreateVerifier()),
            Cancellation)).Value);

        Assert.Equal(OAuthErrors.InvalidGrant, tokens.Code);
    }

    [Fact]
    public async Task A_redirect_other_than_the_one_authorized_is_an_invalid_grant()
    {
        var secret = await _connector.Secret();
        var verifier = EnrolmentPkce.CreateVerifier();
        var code = await _connector.AuthorizationCode(verifier);

        var tokens = Assert.IsType<Error>((await _connector.Exchange.Handle(
            new ExchangeAuthorizationCodeCommand(ConnectorFixture.Credentials(secret), code, "https://claude.ai/elsewhere", verifier),
            Cancellation)).Value);

        Assert.Equal(OAuthErrors.InvalidGrant, tokens.Code);
    }

    [Fact]
    public async Task A_code_works_once_and_its_reuse_revokes_what_it_issued()
    {
        var secret = await _connector.Secret();
        var verifier = EnrolmentPkce.CreateVerifier();
        var code = await _connector.AuthorizationCode(verifier);
        var exchange = new ExchangeAuthorizationCodeCommand(ConnectorFixture.Credentials(secret), code, ConnectorSettings.DefaultRedirectUri, verifier);

        var first = Assert.IsType<Ok<ConnectorTokenResponse>>((await _connector.Exchange.Handle(exchange, Cancellation)).Value).Value;
        var reuse = Assert.IsType<Error>((await _connector.Exchange.Handle(exchange, Cancellation)).Value);
        var refresh = Assert.IsType<Error>((await _connector.Refresh.Handle(
            new RefreshConnectorTokenCommand(ConnectorFixture.Credentials(secret), first.RefreshToken, Scope: null), Cancellation)).Value);

        Assert.Equal(OAuthErrors.InvalidGrant, reuse.Code);
        Assert.Equal(OAuthErrors.InvalidGrant, refresh.Code);
    }

    [Fact]
    public async Task A_refresh_rotates_and_the_presented_token_stops_working()
    {
        var secret = await _connector.Secret();
        var first = Assert.IsType<Ok<ConnectorTokenResponse>>((await _connector.Exchanged(secret)).Value).Value;

        var rotated = Assert.IsType<Ok<ConnectorTokenResponse>>((await _connector.Refresh.Handle(new RefreshConnectorTokenCommand(ConnectorFixture.Credentials(secret), first.RefreshToken, null), Cancellation)).Value).Value;
        var next = await _connector.Refresh.Handle(new RefreshConnectorTokenCommand(ConnectorFixture.Credentials(secret), rotated.RefreshToken, null), Cancellation);

        Assert.NotEqual(first.RefreshToken, rotated.RefreshToken);
        Assert.NotEqual(first.AccessToken, rotated.AccessToken);
        Assert.IsType<Ok<ConnectorTokenResponse>>(next.Value);
    }

    [Fact]
    public async Task Replaying_a_rotated_refresh_token_revokes_the_family()
    {
        var secret = await _connector.Secret();
        var first = Assert.IsType<Ok<ConnectorTokenResponse>>((await _connector.Exchanged(secret)).Value).Value;
        var rotated = Assert.IsType<Ok<ConnectorTokenResponse>>((await _connector.Refresh.Handle(new RefreshConnectorTokenCommand(ConnectorFixture.Credentials(secret), first.RefreshToken, null), Cancellation)).Value).Value;

        var replay = Assert.IsType<Error>((await _connector.Refresh.Handle(new RefreshConnectorTokenCommand(ConnectorFixture.Credentials(secret), first.RefreshToken, null), Cancellation)).Value);
        var victim = Assert.IsType<Error>((await _connector.Refresh.Handle(new RefreshConnectorTokenCommand(ConnectorFixture.Credentials(secret), rotated.RefreshToken, null), Cancellation)).Value);

        Assert.Equal(OAuthErrors.InvalidGrant, replay.Code);
        Assert.Equal(OAuthErrors.InvalidGrant, victim.Code);
    }

    [Fact]
    public async Task A_refresh_token_older_than_thirty_days_is_an_invalid_grant()
    {
        var secret = await _connector.Secret();
        var first = Assert.IsType<Ok<ConnectorTokenResponse>>((await _connector.Exchanged(secret)).Value).Value;
        _connector.Clock.Advance(TimeSpan.FromDays(30) + TimeSpan.FromSeconds(1));

        var refresh = Assert.IsType<Error>((await _connector.Refresh.Handle(new RefreshConnectorTokenCommand(ConnectorFixture.Credentials(secret), first.RefreshToken, null), Cancellation)).Value);

        Assert.Equal(OAuthErrors.InvalidGrant, refresh.Code);
    }

    [Fact]
    public async Task A_refresh_asking_for_more_scope_is_an_invalid_scope()
    {
        var secret = await _connector.Secret();
        var first = Assert.IsType<Ok<ConnectorTokenResponse>>((await _connector.Exchanged(secret)).Value).Value;

        var refresh = Assert.IsType<Error>((await _connector.Refresh.Handle(
            new RefreshConnectorTokenCommand(ConnectorFixture.Credentials(secret), first.RefreshToken, McpScopes.TasksRead), Cancellation)).Value);

        Assert.Equal(OAuthErrors.InvalidScope, refresh.Code);
    }

    [Fact]
    public async Task Revoking_the_secret_cuts_off_the_secret_and_every_refresh_token()
    {
        var secret = await _connector.Secret();
        var first = Assert.IsType<Ok<ConnectorTokenResponse>>((await _connector.Exchanged(secret)).Value).Value;

        var revoked = await _connector.RevokeSecret.Handle(new RevokeConnectorSecretCommand(_connector.OwnerDevice), Cancellation);
        var withOld = Assert.IsType<Error>((await _connector.Refresh.Handle(new RefreshConnectorTokenCommand(ConnectorFixture.Credentials(secret), first.RefreshToken, null), Cancellation)).Value);
        var newSecret = await _connector.Secret();
        var withNew = Assert.IsType<Error>((await _connector.Refresh.Handle(new RefreshConnectorTokenCommand(ConnectorFixture.Credentials(newSecret), first.RefreshToken, null), Cancellation)).Value);

        Assert.IsType<Ok>(revoked.Value);
        Assert.Equal(OAuthErrors.InvalidClient, withOld.Code);
        Assert.Equal(OAuthErrors.InvalidGrant, withNew.Code);
    }

    [Fact]
    public async Task A_new_secret_replaces_the_old_one()
    {
        var old = await _connector.Secret();
        await _connector.Secret();

        var tokens = Assert.IsType<Error>((await _connector.Exchanged(old)).Value);

        Assert.Equal(OAuthErrors.InvalidClient, tokens.Code);
    }

    [Fact]
    public async Task A_code_issued_before_a_new_secret_is_an_invalid_grant()
    {
        var old = await _connector.Secret();
        var verifier = EnrolmentPkce.CreateVerifier();
        var code = await _connector.AuthorizationCode(verifier);
        var fresh = await _connector.Secret();

        var tokens = Assert.IsType<Error>((await _connector.Exchange.Handle(
            new ExchangeAuthorizationCodeCommand(ConnectorFixture.Credentials(fresh), code, ConnectorSettings.DefaultRedirectUri, verifier), Cancellation)).Value);

        Assert.NotEqual(old, fresh);
        Assert.Equal(OAuthErrors.InvalidGrant, tokens.Code);
    }

    [Fact]
    public async Task The_secret_is_256_bits_shown_once_and_only_its_hash_is_kept()
    {
        var created = Assert.IsType<Ok<ConnectorSecretResponse>>((await _connector.CreateSecret.Handle(new CreateConnectorSecretCommand(_connector.OwnerDevice), Cancellation)).Value).Value;

        var stored = await _connector.Clients.Find(ConnectorSettings.DefaultClientId, Cancellation);
        var status = Assert.IsType<Ok<ConnectorStatusResponse>>((await _connector.Describe.Handle(new DescribeConnectorQuery(_connector.OwnerDevice), Cancellation)).Value).Value;

        Assert.Equal(43, created.ClientSecret.Length);
        Assert.Equal(_connector.Hasher.Hash(created.ClientSecret), stored!.SecretHash);
        Assert.True(status.HasSecret);
        Assert.Equal(_connector.Clock.GetUtcNow(), status.CreatedAt);
        Assert.Equal(new Uri(ConnectorSettings.DefaultRedirectUri), status.RedirectUri);
    }

    [Fact]
    public async Task Only_the_configured_owner_manages_the_secret()
    {
        var stranger = new DomainModels.OwnerScope(new DomainModels.OwnerId(Guid.CreateVersion7()), new DomainModels.DeviceId(Guid.CreateVersion7()));

        var created = Assert.IsType<Error>((await _connector.CreateSecret.Handle(new CreateConnectorSecretCommand(stranger), Cancellation)).Value);
        var revoked = Assert.IsType<Error>((await _connector.RevokeSecret.Handle(new RevokeConnectorSecretCommand(stranger), Cancellation)).Value);
        var described = Assert.IsType<Error>((await _connector.Describe.Handle(new DescribeConnectorQuery(stranger), Cancellation)).Value);

        Assert.Equal(SyncErrorCodes.ConnectorNotOwner, created.Code);
        Assert.Equal(SyncErrorCodes.ConnectorNotOwner, revoked.Code);
        Assert.Equal(SyncErrorCodes.ConnectorNotOwner, described.Code);
    }

    [Fact]
    public async Task Revoking_leaves_no_secret_and_a_new_authorize_unauthorized()
    {
        await _connector.Secret();

        await _connector.RevokeSecret.Handle(new RevokeConnectorSecretCommand(_connector.OwnerDevice), Cancellation);
        var status = Assert.IsType<Ok<ConnectorStatusResponse>>((await _connector.Describe.Handle(new DescribeConnectorQuery(_connector.OwnerDevice), Cancellation)).Value).Value;
        var started = Assert.IsType<Ok<Uri>>((await _connector.Start.Handle(ConnectorFixture.Authorize(EnrolmentPkce.CreateVerifier()), Cancellation)).Value).Value;

        Assert.False(status.HasSecret);
        Assert.Equal(OAuthErrors.UnauthorizedClient, ConnectorFixture.Query(started, "error"));
    }
}
