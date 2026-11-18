using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.Enrolment;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.Connector;

/// <summary>claude.ai's authorization request (RFC 6749 §4.1.1, RFC 7636, RFC 8707), its
/// query parameters as they arrived — each null when absent or repeated.
/// <paramref name="RepeatedParameter"/> names a parameter sent more than once, which RFC 6749
/// §3.1 makes malformed: <c>invalid_request</c>, in place when the client or its redirect
/// cannot be trusted, on the redirect otherwise.</summary>
public sealed record StartConnectorAuthorizationCommand(
    string? ResponseType,
    string? ClientId,
    string? RedirectUri,
    string? CodeChallenge,
    string? CodeChallengeMethod,
    string? Scope,
    string? State,
    string? Resource,
    string? RepeatedParameter = null);

/// <summary>
/// Checks an authorization request and sends the owner's browser to GitHub (local ADR 0052
/// §8). The client and its redirect are checked first and refused in place — a failure —
/// because an unverified redirect must never receive anything (RFC 6749 §4.1.2.1). Every
/// later problem goes back to the registered redirect with <c>error=</c> and the client's
/// state. A valid request is kept by the hash of a state of the service's own; claude.ai's
/// state never goes to GitHub.
/// </summary>
public sealed class StartConnectorAuthorizationCommandHandler(
    IConnectorAuthorizationStore authorizations,
    IConnectorClientStore clients,
    IGitHubOwnerSignIn gitHub,
    OwnerEnrolment owner,
    ConnectorSettings settings,
    ICredentialHasher hasher,
    TimeProvider clock)
    : ICommandHandler<StartConnectorAuthorizationCommand, Result<Uri>>
{
    private const int MaxStateLength = 1024;

    public async Task<Result<Uri>> Handle(StartConnectorAuthorizationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = ConnectorRefusals.Start("sync.connector.authorize");

        // A repeated client_id or redirect_uri arrives as null, so it is refused here, in place.
        if (!string.Equals(command.ClientId, settings.ClientId, StringComparison.Ordinal))
        {
            return ConnectorRefusals.Refused(OAuthErrors.InvalidRequest, "This client is not registered with the Backlog sync service.");
        }

        if (!string.Equals(command.RedirectUri, settings.RedirectUri, StringComparison.Ordinal))
        {
            return ConnectorRefusals.Refused(OAuthErrors.InvalidRequest, "The redirect URI is not the one registered for this client.");
        }

        var redirect = settings.RedirectUri;
        var state = command.State;

        if (command.RepeatedParameter is { } repeated)
        {
            return ConnectorRedirects.Error(redirect, state, OAuthErrors.InvalidRequest, $"The {repeated} parameter was sent more than once.");
        }

        if (state is { Length: > MaxStateLength })
        {
            return ConnectorRedirects.Error(redirect, null, OAuthErrors.InvalidRequest, "The state is too long.");
        }

        if (!string.Equals(command.ResponseType, "code", StringComparison.Ordinal))
        {
            return ConnectorRedirects.Error(redirect, state, OAuthErrors.UnsupportedResponseType, "Only the authorization code flow is supported.");
        }

        if (!string.Equals(command.CodeChallengeMethod, EnrolmentPkce.Method, StringComparison.Ordinal)
            || !EnrolmentPkce.IsWellFormedChallenge(command.CodeChallenge))
        {
            return ConnectorRedirects.Error(redirect, state, OAuthErrors.InvalidRequest, "An S256 code challenge is required.");
        }

        var scopes = ConnectorScopes.Parse(command.Scope);
        if (scopes is null)
        {
            return ConnectorRedirects.Error(redirect, state, OAuthErrors.InvalidScope, "Only inbox:capture can be granted.");
        }

        if (settings.Resource is not { } resource || !owner.RequiresSignIn)
        {
            return ConnectorRedirects.Error(redirect, state, OAuthErrors.TemporarilyUnavailable, "This sync service has no owner or connector configured.");
        }

        if (command.Resource is not null && !string.Equals(command.Resource, resource, StringComparison.Ordinal))
        {
            return ConnectorRedirects.Error(redirect, state, OAuthErrors.InvalidTarget, "Tokens are issued for the Backlog MCP resource only.");
        }

        var client = await ConnectorClients.Current(clients, settings, cancellationToken);
        if (!client.HasSecret)
        {
            return ConnectorRedirects.Error(redirect, state, OAuthErrors.UnauthorizedClient, "No client secret exists. Create one in Backlog's MCP server settings.");
        }

        var signInState = EnrolmentCodes.NewSecret();

        await authorizations.Add(
            new ConnectorAuthorization(
                hasher.Hash(signInState),
                ConnectorAuthorizationKind.SignIn,
                command.CodeChallenge!,
                redirect,
                state,
                string.Join(' ', scopes),
                resource,
                client.Generation,
                Guid.CreateVersion7(),
                clock.GetUtcNow() + ConnectorRedirects.SignInLifetime),
            cancellationToken);

        return gitHub.AuthorizeAddress(signInState);
    }
}
