using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Features.Enrolment;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Backlog.Modules.Sync.Features.Connector;

/// <summary><c>grant_type=refresh_token</c> (RFC 6749 §6).</summary>
public sealed record RefreshConnectorTokenCommand(ConnectorClientCredentials Client, string? RefreshToken, string? Scope);

/// <summary>
/// Rotates a refresh token (local ADR 0058 §8, inherited ADR 0012): the answer carries a new
/// refresh token, and the one presented stops working in the same step. A token that was
/// already rotated or revoked is a replay, and revokes its whole family — the legitimate
/// holder then has to connect again, which is the point. There is deliberately no grace for
/// a retried refresh whose answer was lost: that retry is a replay too (inherited ADR 0012).
/// A token issued under an earlier client generation — before the secret was revoked or
/// replaced — is dead, and so is one whose owner or resource is no longer the configured one.
/// <para>
/// The rotation runs first, with the replacement drafted beforehand; the last-used stamp, the
/// access token and <c>tokens_issued</c> follow only for the caller that won it.
/// </para>
/// </summary>
public sealed partial class RefreshConnectorTokenCommandHandler(
    IConnectorClientStore clients,
    IConnectorRefreshTokenStore refreshTokens,
    IConnectorTokenIssuer issuer,
    OwnerEnrolment owner,
    ConnectorSettings settings,
    ICredentialHasher hasher,
    TimeProvider clock,
    ILogger<RefreshConnectorTokenCommandHandler> log)
    : ICommandHandler<RefreshConnectorTokenCommand, Result<ConnectorTokenResponse>>
{
    private const string GrantType = "refresh_token";

    public async Task<Result<ConnectorTokenResponse>> Handle(RefreshConnectorTokenCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = ConnectorRefusals.Start("sync.connector.refresh");
        activity?.SetTag(SyncTelemetry.GrantTypeTag, GrantType);

        var client = await ConnectorTokenGrants.Authenticate(clients, settings, hasher, command.Client, cancellationToken);
        if (client is null)
        {
            return ConnectorTokenGrants.InvalidClient();
        }

        if (string.IsNullOrEmpty(command.RefreshToken))
        {
            return ConnectorTokenGrants.Refused(OAuthErrors.InvalidRequest, "The refresh token is required.");
        }

        var presentedHash = hasher.Hash(command.RefreshToken);
        var presented = await refreshTokens.FindByHash(presentedHash, cancellationToken);

        if (presented is null)
        {
            return ConnectorTokenGrants.InvalidGrant("The refresh token is not one this service issued.");
        }

        if (presented.IsSpent)
        {
            return await Replayed(presented.FamilyId, cancellationToken);
        }

        var now = clock.GetUtcNow();
        if (presented.HasExpired(now) || presented.ClientGeneration != client.Generation)
        {
            return ConnectorTokenGrants.InvalidGrant("The refresh token has expired or was revoked.");
        }

        // A change of owner or resource retires what was granted under the old one.
        if (owner.Owner != presented.Owner || !string.Equals(settings.Resource, presented.Resource, StringComparison.Ordinal))
        {
            return ConnectorTokenGrants.InvalidGrant("The refresh token was issued for another owner or resource.");
        }

        var scope = presented.Scope;
        if (command.Scope is not null)
        {
            var granted = presented.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var asked = command.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (asked.Length == 0 || asked.Except(granted, StringComparer.Ordinal).Any())
            {
                return ConnectorTokenGrants.Refused(OAuthErrors.InvalidScope, "A refresh cannot widen the granted scope.");
            }

            scope = string.Join(' ', asked.Distinct(StringComparer.Ordinal));
        }

        var grant = new ConnectorGrant(presented.Owner, scope, presented.Resource, presented.FamilyId);
        var replacement = ConnectorTokenGrants.DraftRefreshToken(hasher, settings, now, client, grant);

        // Lost the race to another caller holding the same token, or the family was revoked
        // meanwhile: that is a replay too, and nothing was issued.
        if (!await refreshTokens.TryRotate(presentedHash, replacement.Stored, cancellationToken))
        {
            return await Replayed(presented.FamilyId, cancellationToken);
        }

        return await ConnectorTokenGrants.Issue(clients, issuer, now, client, grant, replacement, GrantType, cancellationToken);
    }

    private async Task<Error> Replayed(Guid familyId, CancellationToken cancellationToken)
    {
        LogReplay(log, familyId);
        await refreshTokens.RevokeFamily(familyId, cancellationToken);
        return ConnectorTokenGrants.InvalidGrant("The refresh token was already used.");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connector refresh token replayed; revoking refresh-token family {FamilyId}.")]
    private static partial void LogReplay(ILogger logger, Guid familyId);
}
