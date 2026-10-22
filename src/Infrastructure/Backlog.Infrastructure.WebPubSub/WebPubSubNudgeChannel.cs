using System.Text.Json;

using Azure.Core;
using Azure.Messaging.WebPubSub;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

using Microsoft.Extensions.Logging;

namespace Backlog.Infrastructure.WebPubSub;

/// <summary>
/// The realtime nudge over Azure Web PubSub (local ADR 0038 §4): one group per owner on the
/// <c>notifications</c> hub, a client access URL that joins a device to its own owner's group
/// and grants it nothing else, and a "pull now" message published to that group.
/// <para>
/// Both calls go to the hub's data-plane REST API with the service's managed identity, which
/// holds the Web PubSub Service Owner role there. Neither is allowed to fail the request that
/// made it: a nudge that does not go out costs a desktop at most its two-minute poll, and a
/// negotiate that does not answer is a 503 the desktop retries while it polls. Each call is
/// bounded by <see cref="CallBudget"/>, and the client's own retries are cut to one (see
/// <c>NudgeChannelRegistration</c>).
/// </para>
/// </summary>
internal sealed partial class WebPubSubNudgeChannel(WebPubSubServiceClient client, ILogger<WebPubSubNudgeChannel> log)
    : INotificationNudgeChannel
{
    /// <summary>How long a client access token is good for. The token is checked when the
    /// socket opens and not after, so this bounds only how long a URL may sit unused; a
    /// desktop negotiates again on every reconnect.</summary>
    internal static readonly TimeSpan ClientAccessLifetime = TimeSpan.FromHours(1);

    /// <summary>The group an owner's devices listen on. The owner id is the only thing in
    /// it, and only the service ever names one: a client token carries no role that would let
    /// a connection join another group.</summary>
    internal static string GroupFor(OwnerId owner) => $"owner-{owner.Value:N}";

    /// <summary>How long one call to the hub may take in all, its retries included. The
    /// publish runs inside the push or reply request that caused it, after the write landed, so
    /// a hub that hangs must not hold that request; past this the nudge is given up.</summary>
    internal static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(10);

    public async Task<Uri?> GetClientAccessUrlAsync(OwnerScope scope, CancellationToken cancellationToken = default)
    {
        try
        {
            return await client.GetClientAccessUriAsync(
                    ClientAccessLifetime,
                    userId: scope.DeviceId.Value.ToString("D"),
                    roles: [],
                    groups: [GroupFor(scope.OwnerId)],
                    cancellationToken)
                .WaitAsync(CallBudget, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Not null: null means "no hub", which the desktop asks about again only rarely.
            // A hub that failed just now is worth asking again soon.
            LogNegotiateFailed(log, ex);
            throw new SyncReplicaException(
                SyncErrorCodes.NudgeUnavailable,
                "The nudge hub could not hand out a connection just now; keep polling and ask again shortly.",
                ex);
        }
    }

    public async Task PublishAsync(OwnerId owner, string kind, CancellationToken cancellationToken = default)
    {
        var message = JsonSerializer.Serialize(new NotificationNudgeMessage(NotificationNudgeMessage.Pull, kind), JsonSerializerOptions.Web);

        try
        {
            await client.SendToGroupAsync(GroupFor(owner), message, ContentType.ApplicationJson)
                .WaitAsync(CallBudget, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Whatever the hub did — refused, timed out, retried out into an AggregateException
            // — the write that caused this nudge has landed and must not be reported as failed.
            LogPublishFailed(log, kind, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Web PubSub did not mint a client access URL; the desktop keeps polling and asks again.")]
    private static partial void LogNegotiateFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Web PubSub did not take a {Kind} nudge; the owner's desktops find the change on their next poll.")]
    private static partial void LogPublishFailed(ILogger logger, string kind, Exception exception);
}
