using Backlog.Mobile.UI.Outbox;

namespace Backlog.Mobile.UI.Services;

/// <summary>
/// The app came back to the foreground, or the network came back. Either way
/// the outbox tries now and the screen showing a list refreshes it.
/// <para>
/// The hosts say so in their own words: the MAUI head from its window's
/// <c>Resumed</c> and from <c>Connectivity</c>, both heads from the page's
/// <c>visibilitychange</c> and <c>online</c> events (<see cref="Components.LifecycleBridge"/>).
/// Two reports of one resume cost a second flush that finds nothing due.
/// </para>
/// </summary>
public sealed class AppLifecycle(DeviceOutbox outbox)
{
    /// <summary>Raised after the outbox has been told. May be raised off the
    /// render thread.</summary>
    public event Action? Resumed;

    public void Resume()
    {
        outbox.Resume();
        Resumed?.Invoke();
    }
}
