namespace Backlog.Infrastructure.Sync.Notifications;

/// <summary>
/// How far this owner instance has got in carrying its notifications, and what it decided about
/// the replies it rejected.
/// <para>
/// No watermark: the exchange finds what to push by comparing the open notifications with
/// <see cref="Tracked"/>. A notification it has not pushed is new; one it pushed open that is no
/// longer open has settled here and is pushed again. The owner, the device and the reply poll's
/// last run sit beside it, for the reasons <c>AnnotationSyncState</c> keeps the first two.
/// </para>
/// </summary>
/// <param name="Tracked">Every notification this instance has pushed or still has to.</param>
/// <param name="LastReplyPoll">When the replies were last listed, or null.</param>
/// <param name="OwnerId">The owner the progress was recorded under.</param>
/// <param name="DeviceId">The sync device the progress was recorded under.</param>
public sealed record NotificationSyncState(
    IReadOnlyDictionary<Guid, TrackedNotification> Tracked,
    DateTimeOffset? LastReplyPoll = null,
    Guid? OwnerId = null,
    Guid? DeviceId = null)
{
    /// <summary>A state that has got nowhere.</summary>
    public static NotificationSyncState Nowhere { get; } = new(new Dictionary<Guid, TrackedNotification>());
}

/// <summary>One notification as this exchange last pushed it.</summary>
/// <param name="PushedState">The wire state it was last pushed in, or null when it never was.</param>
/// <param name="Dirty">Whether it changed since that push and has to go again.</param>
/// <param name="RejectedReplies">The replies the owner rejected, carried on every push.</param>
/// <param name="SettledAt">When it left Open, once a push has said so — what lets a settled
/// entry be forgotten after the replica has expired it.</param>
/// <param name="PushedAt">The stamp the last push carried. The next one is always later, so a
/// clock that stepped back cannot make the replica drop a change as stale; and an open
/// notification is pushed again before its replica copy's backstop expiry.</param>
/// <param name="Withheld">Whether the last push left it out — the service would refuse it, or
/// holds its id under another owner instance — so the presence refresh does not offer it, and
/// warn about it, every few minutes.</param>
public sealed record TrackedNotification(
    string? PushedState,
    bool Dirty,
    IReadOnlyList<NotificationReplyRejection> RejectedReplies,
    DateTimeOffset? SettledAt = null,
    DateTimeOffset? PushedAt = null,
    bool Withheld = false);

/// <summary>
/// Where this device keeps its notification-exchange progress between runs — its own store,
/// for the reason every other feed keeps one: the exchanges fail separately.
/// </summary>
public interface INotificationSyncStateStore
{
    /// <summary>Where this device has got to.</summary>
    NotificationSyncState Current { get; }

    /// <summary>Records the state, replacing what was there. Throws and leaves
    /// <see cref="Current"/> alone when it cannot.</summary>
    void Save(NotificationSyncState state);

    /// <summary>Where the progress is kept.</summary>
    string StorePath { get; }
}
