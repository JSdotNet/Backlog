namespace Backlog.Mobile.UI.Notifications;

/// <summary>
/// The From and Repository choices of the phone's Notifications tab, kept on this device
/// (<c>.devbook/domain/notifications/requirements.md</c> — "The phone remembers its filters until
/// the person clears them").
/// </summary>
/// <remarks>
/// A device setting rather than one that syncs, as the desktop centre keeps its own: what a
/// person narrows this phone's tab to is this phone's. The Android head keeps it in the app's
/// preferences, so it outlives a launch; the browser harness and the tests keep it for as long as
/// the process. Both views read the same choice, so it survives switching between them.
/// </remarks>
public interface INotificationFilterStore
{
    /// <summary>The choices last made; <see cref="NotificationFilter.None"/> until one is.</summary>
    NotificationFilter Current { get; set; }
}

/// <summary><see cref="INotificationFilterStore"/> for as long as the process: the browser
/// harness's and the tests'.</summary>
public sealed class InMemoryNotificationFilterStore : INotificationFilterStore
{
    private NotificationFilter _current = NotificationFilter.None;

    /// <inheritdoc />
    public NotificationFilter Current
    {
        get => Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, value ?? NotificationFilter.None);
    }
}
