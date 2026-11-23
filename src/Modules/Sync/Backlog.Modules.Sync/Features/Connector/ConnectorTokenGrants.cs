using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.Enrolment;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.Connector;

/// <summary>The client authentication a token call carried, from the Basic header or the
/// form (RFC 6749 §2.3.1).</summary>
public sealed record ConnectorClientCredentials(string? ClientId, string? ClientSecret);

/// <summary>What one grant hands out: whose tokens, for which scope and resource, in which
/// refresh-token family.</summary>
internal sealed record ConnectorGrant(OwnerId Owner, string Scope, string Resource, Guid FamilyId);

/// <summary>A refresh token about to be issued: the plaintext the answer carries, and the
/// record the store keeps by its hash.</summary>
internal sealed record ConnectorRefreshTokenDraft(string Plaintext, ConnectorRefreshToken Stored);

/// <summary>The parts both grants share: authenticating the client, drafting the refresh
/// token, and issuing the access token beside it.</summary>
internal static class ConnectorTokenGrants
{
    /// <summary>The client the credentials authenticate, or null. The secret is compared
    /// with the stored hash in constant time; a client with no secret authenticates nobody.</summary>
    internal static async Task<ConnectorClient?> Authenticate(
        IConnectorClientStore clients,
        ConnectorSettings settings,
        ICredentialHasher hasher,
        ConnectorClientCredentials credentials,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(credentials.ClientId, settings.ClientId, StringComparison.Ordinal)
            || string.IsNullOrEmpty(credentials.ClientSecret))
        {
            return null;
        }

        var client = await clients.Find(settings.ClientId, cancellationToken);

        return client is { SecretHash: { } hash } && hasher.Matches(credentials.ClientSecret, hash) ? client : null;
    }

    /// <summary>A fresh refresh token of the grant's family, not yet stored: a refresh rotates
    /// it in before anything is issued, and a code exchange adds it.</summary>
    internal static ConnectorRefreshTokenDraft DraftRefreshToken(
        ICredentialHasher hasher,
        ConnectorSettings settings,
        DateTimeOffset now,
        ConnectorClient client,
        ConnectorGrant grant)
    {
        var plaintext = EnrolmentCodes.NewSecret();

        return new ConnectorRefreshTokenDraft(
            plaintext,
            new ConnectorRefreshToken(
                hasher.Hash(plaintext),
                grant.FamilyId,
                client.Generation,
                grant.Owner,
                grant.Scope,
                grant.Resource,
                now,
                now + settings.RefreshTokenLifetime));
    }

    /// <summary>Stamps the client's last use and signs the access token, answered with the
    /// refresh token already stored. Called only once the grant has won, so the stamp and
    /// <c>tokens_issued</c> count tokens actually handed out.</summary>
    internal static async Task<ConnectorTokenResponse> Issue(
        IConnectorClientStore clients,
        IConnectorTokenIssuer issuer,
        DateTimeOffset now,
        ConnectorClient client,
        ConnectorGrant grant,
        ConnectorRefreshTokenDraft refresh,
        string grantType,
        CancellationToken cancellationToken)
    {
        await clients.StampLastUsed(client.ClientId, client.Generation, now, cancellationToken);

        var access = issuer.Issue(new ConnectorTokenClaims(grant.Owner, client.ClientId, grant.Scope, grant.Resource, client.Generation));

        SyncTelemetry.ConnectorTokensIssued.Add(1, new KeyValuePair<string, object?>(SyncTelemetry.GrantTypeTag, grantType));

        return new ConnectorTokenResponse(
            access.AccessToken,
            "Bearer",
            (int)Math.Round((access.ExpiresAt - now).TotalSeconds),
            refresh.Plaintext,
            grant.Scope);
    }

    internal static Error InvalidClient() =>
        ConnectorRefusals.Refused(OAuthErrors.InvalidClient, "Client authentication failed.");

    internal static Error InvalidGrant(string description) =>
        ConnectorRefusals.Refused(OAuthErrors.InvalidGrant, description);

    internal static Error Refused(string code, string description) =>
        ConnectorRefusals.Refused(code, description);
}
