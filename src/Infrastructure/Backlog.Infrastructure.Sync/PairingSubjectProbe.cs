using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The Sync context's answer to the startup probe (<see cref="INotificationSubjectProbe"/>):
/// a pairing is ended only when this process saw it end (<see cref="PairingSubjects.HasEnded"/>).
/// <para>
/// A pairing from an earlier run is unknown here, and is answered live: the only
/// notification about a pairing is the question about the device that redeemed its code,
/// and that device is paired whether or not anyone answers. Closing the question at the next
/// start would keep the device without the person saying so; left open, it waits for Allow or
/// Refuse like any other question, and <see cref="PairingAnswers"/> acts on it from its source
/// key.
/// </para>
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

        return Task.FromResult(!Guid.TryParse(subject.Id, out var id) || !_pairings.HasEnded(id));
    }
}
