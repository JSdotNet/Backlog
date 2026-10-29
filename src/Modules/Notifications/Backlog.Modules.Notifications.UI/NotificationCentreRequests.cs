namespace Backlog.Modules.Notifications.UI;

/// <summary>
/// Asks the shell to open the notification centre, from outside it: a Windows notification's body
/// or its "Open in Backlog" pressed. The host registers one as a singleton. Every window that
/// listens answers (local ADR 0040 §5): the main window's shell opens the centre on the
/// notification named, and a centre already on screen in a window of its own opens on it there. A
/// press made while nobody listens is kept and handed to the next listener that takes it, so a
/// notification pressed before the window is up still opens the centre once it is.
/// </summary>
public sealed class NotificationCentreRequests
{
    private readonly Lock _lock = new();
    private readonly List<Action<Guid?>> _listeners = [];
    private (bool Pending, Guid? Id) _missed;

    /// <summary>Asks for the centre, open on <paramref name="notificationId"/> when one is named.</summary>
    public void Open(Guid? notificationId = null)
    {
        Action<Guid?>[] listeners;
        lock (_lock)
        {
            listeners = [.. _listeners];
            if (listeners.Length == 0) _missed = (true, notificationId);
        }

        foreach (var listener in listeners) listener(notificationId);
    }

    /// <summary>
    /// Listens for requests until the returned handle is disposed. Any number listen at once — one
    /// per window that can show the centre — and each is told of every request. With
    /// <paramref name="takeMissed"/> the listener is handed at once a request nobody was listening
    /// for; a centre that is already on screen passes false, since the shell that put it there took
    /// that request.
    /// </summary>
    public IDisposable Listen(Action<Guid?> listener, bool takeMissed = true)
    {
        ArgumentNullException.ThrowIfNull(listener);

        (bool Pending, Guid? Id) missed = default;
        lock (_lock)
        {
            _listeners.Add(listener);
            if (takeMissed)
            {
                missed = _missed;
                _missed = default;
            }
        }

        if (missed.Pending) listener(missed.Id);
        return new Subscription(this, listener);
    }

    private void Stop(Action<Guid?> listener)
    {
        lock (_lock)
        {
            _listeners.Remove(listener);
        }
    }

    private sealed class Subscription(NotificationCentreRequests owner, Action<Guid?> listener) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Stop(listener);
        }
    }
}
