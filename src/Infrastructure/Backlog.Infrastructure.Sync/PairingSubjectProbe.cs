using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The Sync context's answer to the startup probe (<see cref="INotificationSubjectProbe"/>):
/// a pairing a notification is about lives only while <see cref="PairingSubjects"/> holds
/// it open. At startup that is none, so every pairing notification left from an earlier run
/// closes — its code is gone with the process that issued it.
/// </summary>
public sealed class PairingSubjectProbe(PairingSubjects pairings) : INotificationSubjectProbe
{
    private readonly PairingSubjects _pairings = pairings ?? throw new ArgumentNullException(nameof(pairings));

    public string Context => SyncNotificationSubjects.Context;

    public Task<bool> IsLiveAsync(NotificationSubject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        // A type this context does not name is nothing it can say ended.
        if (subject.Type != SyncNotificationSubjects.Pairing) return Task.FromResult(true);

        return Task.FromResult(Guid.TryParse(subject.Id, out var id) && _pairings.IsOpen(id));
    }
}
