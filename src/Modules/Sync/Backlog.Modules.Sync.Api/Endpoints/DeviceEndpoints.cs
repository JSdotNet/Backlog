using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Security;
using Backlog.Modules.Sync.Features.DescribeOwner;
using Backlog.Modules.Sync.Features.IssueDeviceToken;
using Backlog.Modules.Sync.Features.IssuePairingCode;
using Backlog.Modules.Sync.Features.RedeemPairingCode;
using Backlog.Modules.Sync.Features.RegisterFirstDevice;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Api.Endpoints;

/// <summary>
/// Registration, pairing, and the credential-for-token exchange.
/// <para>
/// Three of the five run anonymously, which is the whole shape of an
/// account-free product: a device with no owner yet has nothing to authenticate
/// as, and what authorizes each of those calls is in the body — a pairing code,
/// or a registration credential — rather than in a header.
/// </para>
/// </summary>
internal static class DeviceEndpoints
{
    internal static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder sync)
    {
        var devices = sync.MapGroup(string.Empty).WithTags("Devices");

        devices.MapPost(SyncRoutes.RegisterDevice, RegisterDevice)
            .AllowAnonymous()
            .WithSummary("Registers the first device and mints an owner for it.");

        devices.MapPost(SyncRoutes.RedeemPairingCode, RedeemPairingCode)
            .AllowAnonymous()
            .WithSummary("Joins an existing owner by redeeming a pairing code.");

        devices.MapPost(SyncRoutes.DeviceToken, IssueDeviceToken)
            .AllowAnonymous()
            .WithSummary("Exchanges a registration credential for a short-lived device token.");

        var paired = devices.MapGroup(string.Empty)
            .RequireAuthorization(SyncPolicies.PairedDevice)
            .RequireOwnerScope();

        paired.MapPost(SyncRoutes.PairingCodes, IssuePairingCode)
            .WithSummary("Mints a single-use pairing code for a second device.");

        paired.MapGet(SyncRoutes.DeviceStatus, DescribeOwner)
            .WithSummary("Describes the calling device and its owner.");

        return sync;
    }

    private static async Task<IResult> RegisterDevice(
        HttpContext context,
        RegisterDeviceRequest request,
        ICommandHandler<RegisterFirstDeviceCommand, Result<DeviceRegistrationResponse>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new RegisterFirstDeviceCommand(request.DeviceName), cancellationToken);

        return SyncResults.From(
            context,
            result,
            registration => Results.Created(SyncRoutes.Absolute(SyncRoutes.DeviceStatus), registration));
    }

    private static async Task<IResult> RedeemPairingCode(
        HttpContext context,
        RedeemPairingCodeRequest request,
        ICommandHandler<RedeemPairingCodeCommand, Result<DeviceRegistrationResponse>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(
            new RedeemPairingCodeCommand(request.Code, request.DeviceName), cancellationToken);

        return SyncResults.From(
            context,
            result,
            registration => Results.Created(SyncRoutes.Absolute(SyncRoutes.DeviceStatus), registration));
    }

    private static async Task<IResult> IssueDeviceToken(
        HttpContext context,
        DeviceTokenRequest request,
        ICommandHandler<IssueDeviceTokenCommand, Result<DeviceTokenResponse>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(
            new IssueDeviceTokenCommand(request.DeviceId, request.Credential), cancellationToken);

        return SyncResults.From(context, result, Results.Ok);
    }

    private static async Task<IResult> IssuePairingCode(
        HttpContext context,
        ICommandHandler<IssuePairingCodeCommand, Result<PairingCodeResponse>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new IssuePairingCodeCommand(context.GetOwnerScope()), cancellationToken);

        return SyncResults.From(
            context,
            result,
            code => Results.Created(SyncRoutes.Absolute(SyncRoutes.PairingCodes), code));
    }

    private static async Task<IResult> DescribeOwner(
        HttpContext context,
        IQueryHandler<DescribeOwnerQuery, Result<DeviceStatusResponse>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new DescribeOwnerQuery(context.GetOwnerScope()), cancellationToken);

        return SyncResults.From(context, result, Results.Ok);
    }
}
