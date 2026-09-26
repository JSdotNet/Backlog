using Backlog.Infrastructure.Sync;
using Backlog.Mobile.UI.Outbox;
using Backlog.UI.Components.Shell;

namespace Backlog.Mobile.UI.Services;

/// <summary>
/// The status line's numbers: whether the device is paired, from the credential
/// store; what is waiting, from the outbox; and when the Inbox last pulled, from
/// the pull itself — kept in the device store, so a phone opened on a train
/// still says when it last heard from the service.
/// <para>
/// Offline means the last contact failed, a pull or a send. It is not "something
/// is queued": a capture on its way out for the half-second the post takes is
/// not a phone that is offline.
/// </para>
/// </summary>
public sealed class SyncStatusTracker : ISyncStatusSource, IDisposable
{
    private readonly IDeviceCredentialStore _credentials;
    private readonly DeviceOutbox _outbox;
    private readonly TimeProvider _clock;

    private DateTimeOffset? _lastSyncedAt;
    private bool _pullFailed;

    public SyncStatusTracker(IDeviceCredentialStore credentials, DeviceOutbox outbox, IDeviceStore store, TimeProvider clock)
    {
        _credentials = credentials;
        _outbox = outbox;
        _clock = clock;

        _lastSyncedAt = store.ReadInbox()?.PulledAt;

        _credentials.Changed += OnCredentialsChanged;
        _outbox.Changed += OnOutboxChanged;
    }

    public event Action? Changed;

    public SyncStatusReading Current =>
        _credentials.Current is null ? SyncStatusReading.NotPaired
        : _pullFailed || _outbox.Unreachable ? SyncStatusReading.Offline(_outbox.Entries.Count, _lastSyncedAt)
        : SyncStatusReading.Synced(_lastSyncedAt);

    /// <summary>The Inbox pulled. Whatever was offline about the pull is not any more.</summary>
    public void RecordSynced()
    {
        _lastSyncedAt = _clock.GetUtcNow();
        _pullFailed = false;
        Changed?.Invoke();
    }

    /// <summary>The Inbox could not pull.</summary>
    public void RecordUnreachable()
    {
        _pullFailed = true;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _credentials.Changed -= OnCredentialsChanged;
        _outbox.Changed -= OnOutboxChanged;
    }

    private void OnOutboxChanged() => Changed?.Invoke();

    /// <summary>A new pairing starts from nothing: the last sync time belonged to
    /// the credential that was replaced or forgotten.</summary>
    private void OnCredentialsChanged()
    {
        _lastSyncedAt = null;
        _pullFailed = false;
        Changed?.Invoke();
    }
}
