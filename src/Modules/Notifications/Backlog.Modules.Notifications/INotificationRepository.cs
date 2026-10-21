using Backlog.Modules.Notifications.DomainModels;

namespace Backlog.Modules.Notifications;

/// <summary>
/// Local-first persistence for the <see cref="Notification"/> aggregate on its owner instance:
/// whole-notification load and save, because a notification is one consistency boundary.
/// <para>
/// An internal port, deliberately not registered by <c>AddNotificationsModule()</c>: which
/// adapter implements it is the host's decision — today one row per notification in the same
/// SQLite database the tasks live in (<c>notifications</c>), as local ADR 0003 does for tasks
/// and ADR 0038 §3 asks of the owner.
/// </para>
/// </summary>
public interface INotificationRepository
{
    /// <summary>One notification, or null when none has that id.</summary>
    Task<Notification?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every Open notification, oldest first.</summary>
    Task<IReadOnlyList<Notification>> ListOpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Every notification with an accepted answer its handler has not taken yet,
    /// oldest first.</summary>
    Task<IReadOnlyList<Notification>> ListAwaitingDeliveryAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the whole notification, atomically from a reader's point of view.</summary>
    Task SaveAsync(Notification notification, CancellationToken cancellationToken = default);
}
