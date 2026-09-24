using Backlog.Infrastructure.Sync;
using Backlog.UI.Components.Shell;

namespace Backlog.Mobile.UI.Services;

/// <summary>
/// The status the shell can know today: whether the device is paired, from the
/// credential store, and when the Inbox last heard back from the service.
/// <para>
/// What it cannot know yet is what is waiting to be sent — there is no outbox,
/// so nothing is ever queued and nothing here claims otherwise. <see cref="RecordOffline"/>
/// is the seam the outbox reports through when it lands.
/// </para>
/// </summary>
public sealed class SyncStatusTracker : ISyncStatusSource, IDisposable
{
    private readonly IDeviceCredentialStore _credentials;
    private readonly TimeProvider _clock;

    private DateTimeOffset? _lastSyncedAt;
    private int? _waitingWhileOffline;

    public SyncStatusTracker(IDeviceCredentialStore credentials, TimeProvider clock)
    {
        _credentials = credentials;
        _clock = clock;

        _credentials.Changed += OnCredentialsChanged;
    }

    public event Action? Changed;

    public SyncStatusReading Current =>
        _credentials.Current is null ? SyncStatusReading.NotPaired
        : _waitingWhileOffline is { } waiting ? SyncStatusReading.Offline(waiting, _lastSyncedAt)
        : SyncStatusReading.Synced(_lastSyncedAt);

    /// <summary>The service answered. Whatever was offline is not any more.</summary>
    public void RecordSynced()
    {
        _lastSyncedAt = _clock.GetUtcNow();
        _waitingWhileOffline = null;
        Changed?.Invoke();
    }

    /// <summary>The service could not be reached, with this much queued for it.</summary>
    public void RecordOffline(int waiting)
    {
        _waitingWhileOffline = Math.Max(0, waiting);
        Changed?.Invoke();
    }

    public void Dispose() => _credentials.Changed -= OnCredentialsChanged;

    /// <summary>A new pairing starts from nothing: the last sync time belonged to
    /// the credential that was replaced or forgotten.</summary>
    private void OnCredentialsChanged()
    {
        _lastSyncedAt = null;
        _waitingWhileOffline = null;
        Changed?.Invoke();
    }
}
