namespace Backlog.Mobile.UI.Services;

/// <summary>
/// A tap on the Capture shortcut of the Android launcher icon, handed from the head to the shell:
/// the layout follows it to the capture sheet over the Inbox. The tap that launched the app
/// arrives before the layout exists, so a request waits here until something subscribes — the
/// shape of <see cref="Notifications.NotificationOpenRequests"/>, and for the same reason.
/// A singleton: the head's activity raises it from outside any scope.
/// </summary>
public sealed class CaptureOpenRequests
{
    private readonly Lock _gate = new();
    private readonly List<Action> _subscribers = [];

    private bool _unfollowed;

    /// <summary>Opens the capture sheet now, or as soon as the shell listens.</summary>
    public void Request()
    {
        Action[] listeners;

        lock (_gate)
        {
            listeners = [.. _subscribers];

            // Two taps before the app is drawn still open one sheet.
            if (listeners.Length == 0) _unfollowed = true;
        }

        foreach (var listener in listeners) listener();
    }

    /// <summary>Calls <paramref name="onOpen"/> for every request from now on, starting with
    /// one already waiting. Dispose the answer to stop.</summary>
    public IDisposable Subscribe(Action onOpen)
    {
        ArgumentNullException.ThrowIfNull(onOpen);

        bool waiting;

        lock (_gate)
        {
            _subscribers.Add(onOpen);
            waiting = _unfollowed;
            _unfollowed = false;
        }

        if (waiting) onOpen();

        return new Subscription(this, onOpen);
    }

    private void Unsubscribe(Action onOpen)
    {
        lock (_gate) _subscribers.Remove(onOpen);
    }

    private sealed class Subscription(CaptureOpenRequests owner, Action onOpen) : IDisposable
    {
        private Action? _onOpen = onOpen;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _onOpen, null) is { } subscriber) owner.Unsubscribe(subscriber);
        }
    }
}
