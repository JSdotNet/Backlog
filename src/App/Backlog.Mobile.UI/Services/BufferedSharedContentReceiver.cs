namespace Backlog.Mobile.UI.Services;

/// <summary>
/// The half of <see cref="ISharedContentReceiver"/> that has nothing to do with
/// where a share came from: who is listening, and what to do with a payload that
/// arrives before anyone is.
/// </summary>
/// <remarks>
/// <para>
/// Both hosts need exactly this behaviour and neither can borrow it from the
/// other's implementation, so it lives once, here, and each host's receiver only
/// has to know how to notice a share and call <see cref="Publish"/>. That also
/// means the buffering the screens depend on is covered by tests against this
/// class rather than only through an Android intent nobody can raise in a unit
/// test.
/// </para>
/// <para>
/// A published payload is either delivered or kept, never both: keeping one that
/// a screen has already shown would prefill the field a second time on the next
/// navigation.
/// </para>
/// </remarks>
public abstract class BufferedSharedContentReceiver : ISharedContentReceiver
{
    private readonly object _gate = new();
    private readonly List<Action<SharedContent>> _subscribers = [];

    private SharedContent? _unconsumed;

    /// <inheritdoc />
    public IDisposable Subscribe(Action<SharedContent> onShared)
    {
        ArgumentNullException.ThrowIfNull(onShared);

        SharedContent? waiting;

        lock (_gate)
        {
            _subscribers.Add(onShared);

            waiting = _unconsumed;
            _unconsumed = null;
        }

        // Outside the lock: the callback renders a component, which is neither
        // quick nor something to hold a lock across.
        if (waiting is not null) onShared(waiting);

        return new Subscription(this, onShared);
    }

    /// <summary>
    /// Hands a share to whoever is listening, or keeps it until someone is.
    /// </summary>
    /// <remarks>
    /// A share carrying nothing is dropped rather than buffered: the Android
    /// share sheet can produce one, and replaying it later would show the "shared
    /// from another app" line over an empty field.
    /// </remarks>
    protected void Publish(SharedContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.IsEmpty) return;

        Action<SharedContent>[] listeners;

        lock (_gate)
        {
            listeners = [.. _subscribers];

            // Only the most recent share is worth keeping. An older one is a
            // draft the person has moved on from.
            if (listeners.Length == 0) _unconsumed = content;
        }

        foreach (var listener in listeners) listener(content);
    }

    private void Unsubscribe(Action<SharedContent> onShared)
    {
        lock (_gate) _subscribers.Remove(onShared);
    }

    /// <summary>The token a screen disposes on teardown. A component that has
    /// gone away must not be called into again, and in the MAUI head this
    /// receiver outlives every component it ever served.</summary>
    private sealed class Subscription(BufferedSharedContentReceiver receiver, Action<SharedContent> onShared)
        : IDisposable
    {
        private Action<SharedContent>? _onShared = onShared;

        public void Dispose()
        {
            var subscriber = Interlocked.Exchange(ref _onShared, null);
            if (subscriber is null) return;

            receiver.Unsubscribe(subscriber);
        }
    }
}
