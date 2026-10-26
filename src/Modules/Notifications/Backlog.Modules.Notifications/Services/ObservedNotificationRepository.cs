using Backlog.Modules.Notifications.DomainModels;

namespace Backlog.Modules.Notifications.Services;

/// <summary>
/// The host's <see cref="INotificationRepository"/>, with a <see cref="Saved"/> event after every
/// write. Every change the owner makes — a publish, an answer, an acknowledgement, a close, a
/// delivered mark — is a save, so the notification centre and the operating system's
/// notifications learn of each one here, whichever channel caused it.
/// <para>
/// A listener that throws is the listener's problem: the write is already stored, and nothing a
/// screen does afterwards may make the owner report it as failed.
/// </para>
/// </summary>
internal sealed class ObservedNotificationRepository(INotificationRepository inner) : INotificationRepository
{
    /// <summary>Raised after a notification is stored, with whether that write published it.</summary>
    public event Action<Notification, bool>? Saved;

    public Task<Notification?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        inner.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<Notification>> ListOpenAsync(CancellationToken cancellationToken = default) =>
        inner.ListOpenAsync(cancellationToken);

    public Task<IReadOnlyList<Notification>> ListOpenOrSettledSinceAsync(DateTimeOffset settledSince, CancellationToken cancellationToken = default) =>
        inner.ListOpenOrSettledSinceAsync(settledSince, cancellationToken);

    public Task<IReadOnlyList<Notification>> ListAwaitingDeliveryAsync(CancellationToken cancellationToken = default) =>
        inner.ListAwaitingDeliveryAsync(cancellationToken);

    public async Task SaveAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        // Read before the write only while somebody listens: the first write of an id is the
        // publish, and only a listener needs to tell the two apart.
        var isNew = Saved is not null
            && await inner.GetAsync(notification.Id, cancellationToken).ConfigureAwait(false) is null;

        await inner.SaveAsync(notification, cancellationToken).ConfigureAwait(false);

        if (Saved is not { } saved) return;

        foreach (var listener in saved.GetInvocationList().Cast<Action<Notification, bool>>())
        {
            try
            {
                listener(notification, isNew);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Stored already; see the class summary.
            }
        }
    }
}
