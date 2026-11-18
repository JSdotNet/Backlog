using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Security;
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
            .WithSummary("Ends the owner's GitHub sign-in and sends the browser back to the device.");

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

    private static async Task<IResult> CompleteSignIn(
        HttpContext context,
        string? code,
        string? state,
        ICommandHandler<CompleteOwnerSignInCommand, Result<Uri>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new CompleteOwnerSignInCommand(state, code), cancellationToken);

        return SyncResults.From(context, result, loopback => Results.Redirect(loopback.AbsoluteUri));
    }
}
