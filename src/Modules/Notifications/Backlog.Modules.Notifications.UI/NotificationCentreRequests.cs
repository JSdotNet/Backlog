namespace Backlog.Modules.Notifications.UI;

/// <summary>
/// Asks the shell to open the notification centre, from outside it: a Windows notification's body
/// or its "Open in Backlog" pressed. The host registers one as a singleton. Every window that
/// listens answers (local ADR 0042 §5): the main window's shell opens the centre on the
/// notification named, and a centre already on screen in a window of its own opens on it there.
/// <para>
/// A press no shell heard — none on screen yet, or the main window away on another page — is kept
/// and handed to the next shell that listens, so a notification pressed before the window is up
/// still opens the centre once it is. A centre that only follows requests while it is on screen
/// listens without taking that press, and hearing a press does not stop it being kept for a shell.
/// </para>
/// </summary>
public sealed class NotificationCentreRequests
{
    private readonly Lock _lock = new();
    private readonly List<Listener> _listeners = [];
    private (bool Pending, Guid? Id) _missed;

    /// <summary>Asks for the centre, open on <paramref name="notificationId"/> when one is named.</summary>
    public void Open(Guid? notificationId = null)
    {
        Listener[] listeners;
        lock (_lock)
        {
            listeners = [.. _listeners];
            if (!listeners.Any(listener => listener.IsShell)) _missed = (true, notificationId);
        }

        foreach (var listener in listeners) listener.Handler(notificationId);
    }

    /// <summary>
    /// Listens for requests until the returned handle is disposed. Any number listen at once — one
    /// per window that can show the centre — and each is told of every request.
    /// </summary>
    /// <param name="listener">Told of every request, with the notification it names.</param>
    /// <param name="takeMissed">True for a shell, which opens the centre: it is handed at once a
    /// request no shell heard, and a request it hears is not kept for another. False for a centre
    /// already on screen, which only follows requests while it is there.</param>
    public IDisposable Listen(Action<Guid?> listener, bool takeMissed = true)
    {
        ArgumentNullException.ThrowIfNull(listener);

        var entry = new Listener(listener, takeMissed);
        (bool Pending, Guid? Id) missed = default;
        lock (_lock)
        {
            _listeners.Add(entry);
            if (takeMissed)
            {
                missed = _missed;
                _missed = default;
            }
        }

        if (missed.Pending) listener(missed.Id);
        return new Subscription(this, entry);
    }

    private void Stop(Listener listener)
    {
        lock (_lock)
        {
            _listeners.Remove(listener);
        }
    }

    /// <summary>One listener, by reference, so the same delegate listening twice stops twice.</summary>
    private sealed class Listener(Action<Guid?> handler, bool isShell)
    {
        public Action<Guid?> Handler { get; } = handler;

        public bool IsShell { get; } = isShell;
    }

    private sealed class Subscription(NotificationCentreRequests owner, Listener listener) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Stop(listener);
        }
    }
}
