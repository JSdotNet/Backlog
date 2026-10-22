using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.NegotiateNotificationNudge;

/// <summary>A paired desktop asks where to hold its nudge socket (local ADR 0038 §4).</summary>
public sealed record NegotiateNotificationNudgeQuery(OwnerScope Scope);

/// <summary>
/// Hands out a client access URL for the caller's own owner group — the scope comes from the
/// token, so there is nothing in the request to choose another owner with — or none where the
/// host runs without a channel, which tells the desktop to keep polling.
/// </summary>
public sealed class NegotiateNotificationNudgeQueryHandler(INotificationNudgeChannel channel)
    : IQueryHandler<NegotiateNotificationNudgeQuery, Result<NotificationNudgeNegotiateResponse>>
{
    public async Task<Result<NotificationNudgeNegotiateResponse>> Handle(NegotiateNotificationNudgeQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        using var activity = SyncTelemetry.Source.StartActivity("sync.negotiate_notification_nudge", ActivityKind.Internal);
        activity?.SetTag(SyncTelemetry.OwnerIdTag, query.Scope.OwnerId.ToString());
        activity?.SetTag(SyncTelemetry.DeviceIdTag, query.Scope.DeviceId.ToString());

        var url = await channel.GetClientAccessUrlAsync(query.Scope, cancellationToken);

        return new NotificationNudgeNegotiateResponse(url);
    }
}
