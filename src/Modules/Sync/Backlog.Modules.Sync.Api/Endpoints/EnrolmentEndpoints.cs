using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Security;
using Backlog.Modules.Sync.Features.Connector;
using Backlog.Modules.Sync.Features.Enrolment;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Api.Endpoints;

/// <summary>
/// The owner sign-in (local ADR 0052 §1): a device with no owner starts it, GitHub
/// comes back to the callback, and the browser is sent on to the device's loopback
/// with a one-time code — or with <c>error=access_denied</c> for anyone but the owner.
/// Both anonymous: nobody has a token yet.
/// </summary>
internal static class EnrolmentEndpoints
{
    internal static IEndpointRouteBuilder MapEnrolmentEndpoints(this IEndpointRouteBuilder sync)
    {
        sync.MapPost(SyncRoutes.EnrolmentStart, StartEnrolment)
            .AllowAnonymous()
            .RequireRateLimiting(OwnerEnrolmentRegistration.RateLimitPolicy)
            .WithTags("Devices")
            .WithSummary("Starts the owner sign-in that enrols a first device or recovers a lost owner.");

        return sync;
    }

    /// <summary>The callback the GitHub OAuth App is registered with, at the root.</summary>
    internal static IEndpointRouteBuilder MapGitHubCallback(this IEndpointRouteBuilder app)
    {
        app.MapGet(SyncRoutes.GitHubCallback, CompleteSignIn)
            .AllowAnonymous()
            .RequireRateLimiting(OwnerEnrolmentRegistration.RateLimitPolicy)
            .WithTags("Devices")
            .WithSummary("Ends the owner's GitHub sign-in and sends the browser back to the device, or on to the connector's consent page.");

        return app;
    }

    private static async Task<IResult> StartEnrolment(
        HttpContext context,
        StartEnrolmentRequest request,
        ICommandHandler<StartEnrolmentCommand, Result<EnrolmentStartResponse>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(
            new StartEnrolmentCommand(request.RedirectUri, request.CodeChallenge, request.CodeChallengeMethod, request.State),
            cancellationToken);

        return SyncResults.From(context, result, Results.Ok);
    }

    /// <summary>
    /// The one callback the GitHub OAuth App has, shared by two sign-ins: the remote
    /// connector's authorization (local ADR 0052 §8) is asked first whether the state is one
    /// of its own, and anything it does not recognise goes to the enrolment sign-in unchanged.
    /// </summary>
    private static async Task<IResult> CompleteSignIn(
        HttpContext context,
        string? code,
        string? state,
        ConnectorSettings connectorSettings,
        ICommandHandler<CompleteConnectorSignInCommand, Result<ConnectorSignInOutcome>> connector,
        ICommandHandler<CompleteOwnerSignInCommand, Result<Uri>> handler,
        CancellationToken cancellationToken)
    {
        var connectorResult = await connector.Handle(new CompleteConnectorSignInCommand(state, code), cancellationToken);

        if (connectorResult.IsSuccess)
        {
            return connectorResult.Value switch
            {
                { ConsentTicket: { } ticket } => Results.Redirect($"{SyncRoutes.ConnectorConsent}?ticket={Uri.EscapeDataString(ticket)}"),
                { Redirect: { } back } => Results.Redirect(back.AbsoluteUri),
                _ => throw new InvalidOperationException("A connector sign-in answered neither a consent ticket nor a redirect."),
            };
        }

        if (connectorResult.Error.Code != SyncErrorCodes.ConnectorSignInUnknown)
        {
            return ConnectorPages.Refusal(context, connectorSettings, connectorResult.Error.Message);
        }

        var result = await handler.Handle(new CompleteOwnerSignInCommand(state, code), cancellationToken);

        return SyncResults.From(context, result, loopback => Results.Redirect(loopback.AbsoluteUri));
    }
}
