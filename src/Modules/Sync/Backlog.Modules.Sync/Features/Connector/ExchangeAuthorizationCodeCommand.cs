using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Backlog.Modules.Sync.Features.Connector;

/// <summary><c>grant_type=authorization_code</c> (RFC 6749 §4.1.3, RFC 7636 §4.5).</summary>
public sealed record ExchangeAuthorizationCodeCommand(
    ConnectorClientCredentials Client,
    string? Code,
    string? RedirectUri,
    string? CodeVerifier);

/// <summary>
/// Trades an authorization code for the connector's tokens (local ADR 0058 §8). The client
/// authenticates with its secret; the code must be live, issued under the client's current
/// generation, for the exact redirect, and matched by the verifier. It is burned once — and a
/// code presented again revokes the refresh tokens its first use issued, because a second
/// use means it leaked.
/// </summary>
public sealed partial class ExchangeAuthorizationCodeCommandHandler(
    IConnectorAuthorizationStore authorizations,
    IConnectorClientStore clients,
    IConnectorRefreshTokenStore refreshTokens,
    IConnectorTokenIssuer issuer,
    ConnectorSettings settings,
    ICredentialHasher hasher,
    TimeProvider clock,
    ILogger<ExchangeAuthorizationCodeCommandHandler> log)
    : ICommandHandler<ExchangeAuthorizationCodeCommand, Result<ConnectorTokenResponse>>
{
    private const string GrantType = "authorization_code";

    public async Task<Result<ConnectorTokenResponse>> Handle(ExchangeAuthorizationCodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = ConnectorRefusals.Start("sync.connector.exchange_code");
        activity?.SetTag(SyncTelemetry.GrantTypeTag, GrantType);

        var client = await ConnectorTokenGrants.Authenticate(clients, settings, hasher, command.Client, cancellationToken);
        if (client is null)
        {
            return ConnectorTokenGrants.InvalidClient();
        }

        if (string.IsNullOrEmpty(command.Code))
        {
            return ConnectorTokenGrants.Refused(OAuthErrors.InvalidRequest, "The code is required.");
        }

        var key = hasher.Hash(command.Code);
        var code = await authorizations.FindByHash(key, cancellationToken);

        if (code is not { Kind: ConnectorAuthorizationKind.Code, Owner: { } owner })
        {
            return ConnectorTokenGrants.InvalidGrant("The code is not one this service issued.");
        }

        // Burned before anything else is checked, so a wrong verifier spends the code too:
        // nobody gets to try verifiers against one code.
        if (!await authorizations.TryBurn(key, cancellationToken))
        {
            LogCodeReused(log, code.FamilyId);
            await refreshTokens.RevokeFamily(code.FamilyId, cancellationToken);
            return ConnectorTokenGrants.InvalidGrant("The code was already used.");
        }

        var now = clock.GetUtcNow();
        if (code.HasExpired(now)
            || code.ClientGeneration != client.Generation
            || !string.Equals(command.RedirectUri, code.RedirectUri, StringComparison.Ordinal)
            || !EnrolmentPkce.IsWellFormedVerifier(command.CodeVerifier)
            || !EnrolmentPkce.Matches(command.CodeVerifier!, code.CodeChallenge))
        {
            return ConnectorTokenGrants.InvalidGrant("The code, its redirect or its verifier does not match.");
        }

        var grant = new ConnectorGrant(owner, code.Scope, code.Resource, code.FamilyId);
        var refresh = ConnectorTokenGrants.DraftRefreshToken(hasher, settings, now, client, grant);

        // A second use of the code that revoked the family before this add still wins: the
        // store holds a token added to a revoked family as revoked.
        await refreshTokens.Add(refresh.Stored, cancellationToken);

        return await ConnectorTokenGrants.Issue(clients, issuer, now, client, grant, refresh, GrantType, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connector authorization code presented twice; revoking refresh-token family {FamilyId}.")]
    private static partial void LogCodeReused(ILogger logger, Guid familyId);
}
