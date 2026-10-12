using Backlog.Mobile.UI.Outbox;

namespace Backlog.Mobile.UI.Services;

/// <summary>
/// The app came back to the foreground, or the network came back. Either way
/// the outbox tries now and the screen showing a list refreshes it. And the
/// other way: the app is going to the background, where the OS may end it
/// without another word, so a screen holding unsaved words writes them now.
/// <para>
/// The hosts say so in their own words: the MAUI head from its window's
/// <c>Resumed</c> and from <c>Connectivity</c>, both heads from the page's
/// <c>visibilitychange</c> and <c>online</c> events (<see cref="Components.LifecycleBridge"/>).
/// Two reports of one resume cost a second flush that finds nothing due.
/// A pause comes from the MAUI head's window <c>Deactivated</c> and <c>Stopped</c>
/// and from <c>visibilitychange</c> to hidden; two reports of one pause cost a
/// second save that finds nothing unsaved.
/// </para>
/// </summary>
public sealed class AppLifecycle(DeviceOutbox outbox)
{
    /// <summary>Raised after the outbox has been told. May be raised off the
    /// render thread.</summary>
    public event Action? Resumed;

    /// <summary>Raised when the app is going to the background. May be raised
    /// off the render thread.</summary>
    public event Action? Paused;

    public void Resume()
    {
        outbox.Resume();
        Resumed?.Invoke();
    }

    public void Pause() => Paused?.Invoke();
}
