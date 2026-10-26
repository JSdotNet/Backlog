namespace Backlog.Modules.Notifications.UI;

/// <summary>
/// Asks the shell to open the notification centre, from outside it: a Windows notification's body
/// or its "Open in Backlog" pressed. The host registers one as a singleton; the shell listens while
/// it is on screen and opens the centre on the notification named. A press made while no shell
/// listens is kept and handed to the next one that does, so a notification pressed before the
/// window is up still opens the centre once it is.
/// </summary>
public sealed class NotificationCentreRequests
{
    private readonly Lock _lock = new();
    private Action<Guid?>? _listener;
    private (bool Pending, Guid? Id) _missed;

    /// <summary>Asks for the centre, open on <paramref name="notificationId"/> when one is named.</summary>
    public void Open(Guid? notificationId = null)
    {
        Action<Guid?>? listener;
        lock (_lock)
        {
            listener = _listener;
            if (listener is null) _missed = (true, notificationId);
        }

        listener?.Invoke(notificationId);
    }

    /// <summary>
    /// Listens for requests until the returned handle is disposed, and hands over at once a request
    /// nobody was listening for. One listener at a time — the shell on screen.
    /// </summary>
    public IDisposable Listen(Action<Guid?> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        (bool Pending, Guid? Id) missed;
        lock (_lock)
        {
            _listener = listener;
            missed = _missed;
            _missed = default;
        }

        if (missed.Pending) listener(missed.Id);
        return new Subscription(this, listener);
    }

    private void Stop(Action<Guid?> listener)
    {
        lock (_lock)
        {
            if (_listener == listener) _listener = null;
        }
    }

    private sealed class Subscription(NotificationCentreRequests owner, Action<Guid?> listener) : IDisposable
    {
        public void Dispose() => owner.Stop(listener);
    }
}
