using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.Enrolment;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Backlog.Modules.Sync.Features.Connector;

/// <summary>GitHub came back to the shared callback with a state and, when the person signed
/// in, an authorization code.</summary>
public sealed record CompleteConnectorSignInCommand(string? State, string? AuthorizationCode);

/// <summary>Where the browser goes next: back to claude.ai refused, or on to the consent page
/// with its one-time ticket. Exactly one is set.</summary>
public sealed record ConnectorSignInOutcome(Uri? Redirect, string? ConsentTicket);

/// <summary>
/// Ends the GitHub sign-in of a connector authorization (local ADR 0052 §8). A state the
/// connector did not start is <see cref="SyncErrorCodes.ConnectorSignInUnknown"/> and burns
/// nothing, so the callback can hand it to the enrolment sign-in unchanged. The connector's
/// own state is burned first, so it works once; then only the configured owner's GitHub id
/// gets a consent ticket. Anybody else, and a sign-in GitHub did not confirm, goes back to
/// claude.ai with <c>error=access_denied</c> and no code.
/// </summary>
public sealed partial class CompleteConnectorSignInCommandHandler(
    IConnectorAuthorizationStore authorizations,
    IGitHubOwnerSignIn gitHub,
    OwnerEnrolment owner,
    ICredentialHasher hasher,
    TimeProvider clock,
    ILogger<CompleteConnectorSignInCommandHandler> log)
    : ICommandHandler<CompleteConnectorSignInCommand, Result<ConnectorSignInOutcome>>
{
    public async Task<Result<ConnectorSignInOutcome>> Handle(CompleteConnectorSignInCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = ConnectorRefusals.Start("sync.connector.sign_in");

        if (string.IsNullOrEmpty(command.State))
        {
            return Unknown();
        }

        var key = hasher.Hash(command.State);
        var signIn = await authorizations.FindByHash(key, cancellationToken);

        if (signIn is not { Kind: ConnectorAuthorizationKind.SignIn })
        {
            return Unknown();
        }

        // Ours from here on: a used or expired connector state is refused in place rather
        // than tried as an enrolment.
        if (signIn.HasExpired(clock.GetUtcNow()) || !await authorizations.TryBurn(key, cancellationToken))
        {
            return ConnectorRefusals.Refused(OAuthErrors.InvalidRequest, "This sign-in is not one the sync service is waiting for. Connect again from Claude.");
        }

        if (string.IsNullOrEmpty(command.AuthorizationCode))
        {
            return Denied(signIn, "The GitHub sign-in was cancelled.");
        }

        var userId = await gitHub.ResolveUserIdAsync(command.AuthorizationCode, cancellationToken);

        if (userId is null)
        {
            LogUnconfirmed(log);
            return Denied(signIn, "GitHub did not confirm the sign-in.");
        }

        if (userId != owner.GitHubUserId || owner.Owner is not { } ownerId)
        {
            LogNotOwner(log, userId.Value);
            return Denied(signIn, "Only the owner's GitHub account can connect Claude to this sync service.");
        }

        var ticket = EnrolmentCodes.NewSecret();

        await authorizations.Add(
            signIn with
            {
                KeyHash = hasher.Hash(ticket),
                Kind = ConnectorAuthorizationKind.Consent,
                Owner = ownerId,
                ExpiresAt = clock.GetUtcNow() + ConnectorRedirects.ConsentLifetime,
                Redeemed = false,
            },
            cancellationToken);

        return new ConnectorSignInOutcome(Redirect: null, ConsentTicket: ticket);
    }

    private static ConnectorSignInOutcome Denied(ConnectorAuthorization signIn, string description) =>
        new(ConnectorRedirects.Error(signIn.RedirectUri, signIn.ClientState, OAuthErrors.AccessDenied, description), ConsentTicket: null);

    private static Error Unknown() => Error.NotFound(
        SyncErrorCodes.ConnectorSignInUnknown,
        "This state names no connector sign-in.");

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connector sign-in refused: GitHub user {GitHubUserId} is not the configured owner.")]
    private static partial void LogNotOwner(ILogger logger, long gitHubUserId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connector sign-in refused: GitHub did not confirm the authorization code.")]
    private static partial void LogUnconfirmed(ILogger logger);
}
