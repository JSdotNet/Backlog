using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.Enrolment;

/// <summary>A device with no owner starts the owner sign-in (local ADR 0058 §1).</summary>
public sealed record StartEnrolmentCommand(Uri? RedirectUri, string? CodeChallenge, string? CodeChallengeMethod, string? State);

/// <summary>
/// Checks the device's loopback and its PKCE challenge, then keeps the sign-in by the
/// hash of a state of the service's own and answers the GitHub page to open. The
/// device's state never goes to GitHub: GitHub carries the service's, which the
/// callback looks the sign-in up by.
/// <para>
/// With no owner configured — a Development service only — nobody signs in: the code
/// is issued at once and the answer is the loopback itself.
/// </para>
/// </summary>
public sealed class StartEnrolmentCommandHandler(
    IEnrolmentTicketStore tickets,
    IGitHubOwnerSignIn gitHub,
    OwnerEnrolment owner,
    ICredentialHasher hasher,
    TimeProvider clock)
    : ICommandHandler<StartEnrolmentCommand, Result<EnrolmentStartResponse>>
{
    public async Task<Result<EnrolmentStartResponse>> Handle(
        StartEnrolmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!EnrolmentPkce.IsLoopbackRedirect(command.RedirectUri))
        {
            return Invalid("The redirect must be an http loopback address with a port.");
        }

        if (!string.Equals(command.CodeChallengeMethod, EnrolmentPkce.Method, StringComparison.Ordinal)
            || !EnrolmentPkce.IsWellFormedChallenge(command.CodeChallenge))
        {
            return Invalid("The code challenge must be an S256 challenge.");
        }

        if (string.IsNullOrWhiteSpace(command.State) || command.State.Length > 256)
        {
            return Invalid("A state of at most 256 characters is required.");
        }

        var redirect = command.RedirectUri!;
        var challenge = command.CodeChallenge!;

        if (!owner.RequiresSignIn)
        {
            var loopback = await EnrolmentCodes.IssueAsync(
                tickets, hasher, clock, redirect, challenge, command.State, cancellationToken);

            return new EnrolmentStartResponse(loopback);
        }

        var signInState = EnrolmentCodes.NewSecret();

        await tickets.Add(
            new EnrolmentTicket(
                hasher.Hash(signInState),
                EnrolmentTicketKind.SignIn,
                challenge,
                redirect,
                command.State,
                clock.GetUtcNow() + EnrolmentCodes.SignInLifetime),
            cancellationToken);

        return new EnrolmentStartResponse(gitHub.AuthorizeAddress(signInState));
    }

    private static Error Invalid(string message) =>
        Error.Validation(SyncErrorCodes.EnrolmentRequestInvalid, message);
}
