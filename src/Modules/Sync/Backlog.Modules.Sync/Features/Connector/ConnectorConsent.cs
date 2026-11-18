using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.Enrolment;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.Connector;

/// <summary>The consent page asks what it is showing, by its ticket.</summary>
public sealed record DescribeConnectorConsentQuery(string? Ticket);

/// <summary>One scope on the consent page: its name and what it lets the client do.</summary>
public sealed record ConnectorScopeDescription(string Scope, string Description);

/// <summary>What the consent page shows: the client, each scope, how long the connection
/// lasts, and the ticket the page posts back.</summary>
public sealed record ConnectorConsentPrompt(
    string Ticket,
    string ClientName,
    IReadOnlyList<ConnectorScopeDescription> Scopes,
    TimeSpan RefreshTokenLifetime);

/// <summary>The owner's decision on the consent page, with the ticket it carried.</summary>
public sealed record DecideConnectorConsentCommand(string? Ticket, bool Allow);

/// <summary>
/// Reads a consent ticket for the page without spending it: the page may be reloaded, and
/// only the decision burns it.
/// </summary>
public sealed class DescribeConnectorConsentQueryHandler(
    IConnectorAuthorizationStore authorizations,
    ConnectorSettings settings,
    ICredentialHasher hasher,
    TimeProvider clock)
    : IQueryHandler<DescribeConnectorConsentQuery, Result<ConnectorConsentPrompt>>
{
    public async Task<Result<ConnectorConsentPrompt>> Handle(DescribeConnectorConsentQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        using var activity = ConnectorRefusals.Start("sync.connector.consent.describe");

        var consent = await ConnectorConsentTickets.Live(authorizations, hasher, clock, query.Ticket, cancellationToken);
        if (consent is null)
        {
            return ConnectorConsentTickets.Unknown();
        }

        var scopes = consent.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(scope => new ConnectorScopeDescription(scope, ConnectorScopes.Describe(scope)))
            .ToList();

        return new ConnectorConsentPrompt(query.Ticket!, ConnectorSettings.ClientName, scopes, settings.RefreshTokenLifetime);
    }
}

/// <summary>
/// Spends the consent ticket on the owner's decision. Allow mints a one-time authorization
/// code bound to the challenge, the redirect, the scope and the client generation, and sends
/// it to claude.ai with the client's state; Deny sends <c>error=access_denied</c>. An unknown,
/// expired or spent ticket is refused in place.
/// </summary>
public sealed class DecideConnectorConsentCommandHandler(
    IConnectorAuthorizationStore authorizations,
    ICredentialHasher hasher,
    TimeProvider clock)
    : ICommandHandler<DecideConnectorConsentCommand, Result<Uri>>
{
    public async Task<Result<Uri>> Handle(DecideConnectorConsentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = ConnectorRefusals.Start("sync.connector.consent.decide");

        var consent = await ConnectorConsentTickets.Live(authorizations, hasher, clock, command.Ticket, cancellationToken);
        if (consent is null || !await authorizations.TryBurn(consent.KeyHash, cancellationToken))
        {
            return ConnectorConsentTickets.Unknown();
        }

        if (!command.Allow)
        {
            return ConnectorRedirects.Error(consent.RedirectUri, consent.ClientState, OAuthErrors.AccessDenied, "The owner declined.");
        }

        var code = EnrolmentCodes.NewSecret();

        await authorizations.Add(
            consent with
            {
                KeyHash = hasher.Hash(code),
                Kind = ConnectorAuthorizationKind.Code,
                ExpiresAt = clock.GetUtcNow() + ConnectorRedirects.CodeLifetime,
                Redeemed = false,
            },
            cancellationToken);

        return ConnectorRedirects.Answer(consent.RedirectUri, consent.ClientState, ("code", code));
    }
}

internal static class ConnectorConsentTickets
{
    /// <summary>The live, unspent consent record a ticket names, or null.</summary>
    internal static async Task<ConnectorAuthorization?> Live(
        IConnectorAuthorizationStore authorizations,
        ICredentialHasher hasher,
        TimeProvider clock,
        string? ticket,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(ticket))
        {
            return null;
        }

        var consent = await authorizations.FindByHash(hasher.Hash(ticket), cancellationToken);

        return consent is { Kind: ConnectorAuthorizationKind.Consent, Redeemed: false, Owner: not null }
            && !consent.HasExpired(clock.GetUtcNow())
            ? consent
            : null;
    }

    internal static Error Unknown() => ConnectorRefusals.Refused(
        OAuthErrors.InvalidRequest,
        "This consent page has expired or was already answered. Connect again from Claude.");
}
