namespace Backlog.Mobile.UI.Outbox;

/// <summary>
/// The captures whose edit or withdrawal reached the service after the desktop
/// had already decided them. Nothing was lost the person could have kept — the
/// desktop's decision stands — but an edit must never vanish without a word
/// (<c>.devbook/design/interaction-guidelines.md</c>, Conflict Handling), so the
/// outbox kinds record each one here and the Inbox tab says so once.
/// <para>
/// A singleton with no dependencies of its own, because the kinds that write it
/// are built with the outbox and the screen that reads it outlives neither. Held
/// in memory only: a notice for a change made in another run of the app has
/// nobody left to read it.
/// </para>
/// </summary>
public sealed class CaptureDealtWithNotices
{
    private readonly Lock _gate = new();
    private readonly List<Guid> _captures = [];

    /// <summary>Raised after a capture is recorded or the notices are cleared.
    /// May be raised off the UI thread: the outbox flushes in the background.</summary>
    public event Action? Changed;

    /// <summary>The captures not yet shown to the person, oldest first.</summary>
    public IReadOnlyList<Guid> Captures
    {
        get
        {
            lock (_gate) return [.. _captures];
        }
    }

    /// <summary>The desktop had already dealt with <paramref name="captureId"/>
    /// when this phone's change for it arrived.</summary>
    public void Record(Guid captureId)
    {
        lock (_gate)
        {
            if (_captures.Contains(captureId)) return;
            _captures.Add(captureId);
        }

        Changed?.Invoke();
    }

    /// <summary>The person has seen them.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_captures.Count == 0) return;
            _captures.Clear();
        }

        Changed?.Invoke();
    }
}
