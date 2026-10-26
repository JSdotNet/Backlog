using System.Text.Json;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

using Microsoft.Azure.NotificationHubs;
using Microsoft.Extensions.Logging;

namespace Backlog.Infrastructure.NotificationHubs;

/// <summary>
/// The Android push over Azure Notification Hubs (local ADR 0039 §4): one FCM v1 installation
/// per phone, keyed by its sync device and tagged <c>owner:</c> and <c>device:</c>, and a
/// data-only FCM v1 message sent to an owner's tag, which the hub relays to Firebase.
/// <para>
/// Two clients, because the hub takes no managed identity and a SAS rule's rights are fixed
/// (ADR 0039 §5): the Send-only rule sends, and the Manage rule — which Notification Hubs requires
/// to create or delete an installation — keeps the installations. Each call is bounded by
/// <see cref="CallBudget"/>, and the client's own retries are cut to one (see
/// <c>PushChannelRegistration</c>). Nothing logged here names a token, a title or a summary.
/// </para>
/// </summary>
internal sealed partial class NotificationHubsPushChannel(
    INotificationHubClient send,
    INotificationHubClient manage,
    ILogger<NotificationHubsPushChannel> log)
    : INotificationPushChannel
{
    /// <summary>How long one call to the hub may take in all, its retry included. A send runs
    /// inside the owner's push request after the write landed, so a hub that hangs must not hold
    /// that request; past this the call is given up.</summary>
    internal static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(10);

    /// <summary>The tag every phone of <paramref name="owner"/> carries, and the one a push is
    /// sent to. <c>N</c> format, inside the hub's tag alphabet <c>[A-Za-z0-9_@#.:-]</c>.</summary>
    internal static string OwnerTag(OwnerId owner) => $"owner:{owner.Value:N}";

    /// <summary>The tag naming one phone, so its own installation can be told apart.</summary>
    internal static string DeviceTag(DeviceId device) => $"device:{device.Value:N}";

    /// <summary>The installation id of a phone: its sync device, so a phone that registers again
    /// replaces its own installation rather than adding one.</summary>
    internal static string InstallationIdFor(DeviceId device) => device.Value.ToString("D");

    /// <summary>The FCM v1 installation for the calling phone, owner and device from its
    /// token.</summary>
    internal static Installation InstallationFor(OwnerScope scope, string fcmToken) =>
        new FcmV1Installation(InstallationIdFor(scope.DeviceId), fcmToken)
        {
            Tags = [OwnerTag(scope.OwnerId), DeviceTag(scope.DeviceId)],
        };

    /// <summary>
    /// The FCM v1 native body: <c>{"message":{"android":{"priority":"high"},"data":{…}}}</c>.
    /// Data-only — no <c>notification</c> key — so the app's own messaging service runs and
    /// builds the tray entry while the app is in the background, and high priority so Android
    /// delivers it while the phone dozes. Every data value is a string, as FCM v1 requires.
    /// </summary>
    internal static string FcmPayload(NotificationPushMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return JsonSerializer.Serialize(new FcmEnvelope(new FcmMessage(new FcmAndroid("high"), message.Data)), JsonSerializerOptions.Web);
    }

    public async Task<bool> RegisterAsync(OwnerScope scope, string fcmToken, CancellationToken cancellationToken = default)
    {
        try
        {
            await manage.CreateOrUpdateInstallationAsync(InstallationFor(scope, fcmToken), cancellationToken)
                .WaitAsync(CallBudget, cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Not false: false means "no hub", which the phone does not retry. A hub that failed
            // just now is worth asking again.
            LogRegisterFailed(log, scope.DeviceId.Value, ex);
            throw new SyncReplicaException(
                SyncErrorCodes.PushUnavailable,
                "The push hub could not keep this phone's installation just now; register again shortly.",
                ex);
        }
    }

    public Task RemoveAsync(DeviceId device, CancellationToken cancellationToken = default) =>
        manage.DeleteInstallationAsync(InstallationIdFor(device), cancellationToken)
            .WaitAsync(CallBudget, cancellationToken);

    public Task SendAsync(OwnerId owner, NotificationPushMessage message, CancellationToken cancellationToken = default) =>
        send.SendNotificationAsync(new FcmV1Notification(FcmPayload(message)), OwnerTag(owner), cancellationToken)
            .WaitAsync(CallBudget, cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification Hubs did not keep the push installation of device {DeviceId}; the phone registers again later.")]
    private static partial void LogRegisterFailed(ILogger logger, Guid deviceId, Exception exception);

    private sealed record FcmEnvelope(FcmMessage Message);

    private sealed record FcmMessage(FcmAndroid Android, IReadOnlyDictionary<string, string> Data);

    private sealed record FcmAndroid(string Priority);
}
