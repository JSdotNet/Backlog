using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Ports;

/// <summary>
/// The Android push that tells an owner's phones a notification was published or changed state
/// (local ADR 0038 §4). Declared here and implemented outside — Azure Notification Hubs relaying
/// to FCM, deployed — beside <see cref="INotificationNudgeChannel"/>, so the module says what it
/// needs without naming the service.
/// <para>
/// One installation per phone, keyed by its sync device and tagged with its owner and itself.
/// Both come from the caller's validated token, so a phone can only ever be registered under its
/// own owner, and a push to an owner reaches that owner's phones and nobody else's.
/// </para>
/// <para>
/// The replica stays the truth. A push carries the tray text and nothing a phone could not pull,
/// so a lost one costs the phone its buzz and never data.
/// </para>
/// </summary>
public interface INotificationPushChannel
{
    /// <summary>
    /// Creates or replaces the installation for the calling device, with
    /// <paramref name="fcmToken"/> as its FCM v1 channel. Answers false when this host runs
    /// without a hub — the phone then has nothing to retry. A hub that exists but did not take
    /// it just now throws <see cref="SyncReplicaException"/> with
    /// <c>SyncErrorCodes.PushUnavailable</c>, so the phone tries again later.
    /// </summary>
    Task<bool> RegisterAsync(OwnerScope scope, string fcmToken, CancellationToken cancellationToken = default);

    /// <summary>Deletes the installation of <paramref name="device"/>, so a revoked phone is
    /// pushed nothing more. May throw; the caller has already revoked the device and only logs
    /// it.</summary>
    Task RemoveAsync(DeviceId device, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends <paramref name="message"/> to every phone of <paramref name="owner"/>, bounded in
    /// time. May throw when the hub did not take it; the caller's write has already landed and
    /// the caller contains the failure.
    /// </summary>
    Task SendAsync(OwnerId owner, NotificationPushMessage message, CancellationToken cancellationToken = default);
}
