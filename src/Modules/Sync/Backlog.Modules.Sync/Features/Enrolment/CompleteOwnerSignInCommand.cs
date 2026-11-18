using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Backlog.Modules.Sync.Features.Enrolment;

/// <summary>GitHub came back to the callback with the service's state and, when the
/// person signed in, an authorization code.</summary>
public sealed record CompleteOwnerSignInCommand(string? State, string? AuthorizationCode);

/// <summary>
/// Ends an owner sign-in (local ADR 0052 §1). The sign-in is burned first, so its state
/// works once. Then GitHub is asked who signed in, and only the configured owner's
/// numeric id is let through: the device's loopback gets a one-time code. Anybody
/// else — and a sign-in GitHub did not confirm — gets <c>error=access_denied</c> and
/// no code, so the device can say why it stopped.
/// <para>
/// The answer is the loopback address to send the browser to. A state that names no
/// live sign-in has no loopback to go to, and is a failure answered in place.
/// </para>
/// </summary>
public sealed partial class CompleteOwnerSignInCommandHandler(
    IEnrolmentTicketStore tickets,
    IGitHubOwnerSignIn gitHub,
    OwnerEnrolment owner,
    ICredentialHasher hasher,
    TimeProvider clock,
    ILogger<CompleteOwnerSignInCommandHandler> log)
    : ICommandHandler<CompleteOwnerSignInCommand, Result<Uri>>
{
    public async Task<Result<Uri>> Handle(CompleteOwnerSignInCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrEmpty(command.State) || !owner.RequiresSignIn)
        {
            return Unknown();
        }

        var key = hasher.Hash(command.State);
        var signIn = await tickets.FindByHash(key, cancellationToken);

        if (signIn is not { Kind: EnrolmentTicketKind.SignIn }
            || signIn.HasExpired(clock.GetUtcNow())
            || !await tickets.TryBurn(key, cancellationToken))
        {
            return Unknown();
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

        if (userId != owner.GitHubUserId)
        {
            LogNotOwner(log, userId.Value);
            return Denied(signIn, "Only the owner's GitHub account can enrol a device on this sync service.");
        }

        return await EnrolmentCodes.IssueAsync(
            tickets, hasher, clock, signIn.RedirectUri, signIn.CodeChallenge, signIn.ClientState, cancellationToken);
    }

    private static Uri Denied(EnrolmentTicket signIn, string description) =>
        EnrolmentCodes.Loopback(
            signIn.RedirectUri,
            ("error", "access_denied"),
            ("error_description", description),
            ("state", signIn.ClientState));

    private static Error Unknown() => Error.Validation(
        SyncErrorCodes.EnrolmentSignInUnknown,
        "This sign-in is not one the sync service is waiting for. Start again from Backlog.");

    [LoggerMessage(Level = LogLevel.Warning, Message = "Owner sign-in refused: GitHub user {GitHubUserId} is not the configured owner.")]
    private static partial void LogNotOwner(ILogger logger, long gitHubUserId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Owner sign-in refused: GitHub did not confirm the authorization code.")]
    private static partial void LogUnconfirmed(ILogger logger);
}
