using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.RegisterPushInstallation;

/// <summary>A paired phone hands over its FCM token (local ADR 0039 §4). The scope comes from
/// the token, the FCM token from the body, and nothing in the body can name an owner.</summary>
public sealed record RegisterPushInstallationCommand(OwnerScope Scope, string FcmToken);

/// <summary>
/// Keeps one push installation for the calling device, tagged with its owner and itself, and
/// answers whether it did — never, where the host runs without a hub. A hub that did not take it
/// just now surfaces as the channel's <c>SyncErrorCodes.PushUnavailable</c>, a 503 the phone
/// retries later. The FCM token is never logged or traced.
/// </summary>
public sealed class RegisterPushInstallationCommandHandler(INotificationPushChannel channel)
    : ICommandHandler<RegisterPushInstallationCommand, Result<RegisterPushInstallationResponse>>
{
    public async Task<Result<RegisterPushInstallationResponse>> Handle(RegisterPushInstallationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = SyncTelemetry.Source.StartActivity("sync.register_push_installation", ActivityKind.Internal);
        activity?.SetTag(SyncTelemetry.OwnerIdTag, command.Scope.OwnerId.ToString());
        activity?.SetTag(SyncTelemetry.DeviceIdTag, command.Scope.DeviceId.ToString());

        var registered = await channel.RegisterAsync(command.Scope, command.FcmToken, cancellationToken);

        return new RegisterPushInstallationResponse(registered);
    }
}
