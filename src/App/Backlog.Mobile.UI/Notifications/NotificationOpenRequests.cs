namespace Backlog.Mobile.UI.Notifications;

/// <summary>
/// A tap on a notification in the tray, handed from the head to the shell (local ADR 0039 §4):
/// the layout follows it to the notification's detail. The tap that launched the app arrives
/// before the layout exists, so the last request waits here until something subscribes — the
/// shape of <see cref="Services.BufferedSharedContentReceiver"/>, and for the same reason.
/// A singleton: the head's activity raises it from outside any scope.
/// </summary>
public sealed class NotificationOpenRequests
{
    private readonly Lock _gate = new();
    private readonly List<Action<Guid>> _subscribers = [];

    private Guid? _unfollowed;

    /// <summary>Opens notification <paramref name="id"/> now, or as soon as the shell listens.</summary>
    public void Request(Guid id)
    {
        Action<Guid>[] listeners;

        lock (_gate)
        {
            listeners = [.. _subscribers];

            // Only the latest tap is kept: two taps before the app is drawn mean the second.
            if (listeners.Length == 0) _unfollowed = id;
        }

        foreach (var listener in listeners) listener(id);
    }

    /// <summary>Calls <paramref name="onOpen"/> for every request from now on, starting with
    /// one already waiting. Dispose the answer to stop.</summary>
    public IDisposable Subscribe(Action<Guid> onOpen)
    {
        ArgumentNullException.ThrowIfNull(onOpen);

        Guid? waiting;

        lock (_gate)
        {
            _subscribers.Add(onOpen);
            waiting = _unfollowed;
            _unfollowed = null;
        }

        if (waiting is { } id) onOpen(id);

        return new Subscription(this, onOpen);
    }

    private void Unsubscribe(Action<Guid> onOpen)
    {
        lock (_gate) _subscribers.Remove(onOpen);
    }

    private sealed class Subscription(NotificationOpenRequests owner, Action<Guid> onOpen) : IDisposable
    {
        private Action<Guid>? _onOpen = onOpen;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _onOpen, null) is { } subscriber) owner.Unsubscribe(subscriber);
        }
    }
}
