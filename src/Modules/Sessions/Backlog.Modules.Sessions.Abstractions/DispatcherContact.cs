namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// When a dispatcher last reached this application on its wait route — dispatcher
/// contact (<c>.devbook/domain/sessions/domain.md#session-request-queue</c>, local ADR
/// 0038 §4) — so the queue can warn that nobody is there to answer a request.
/// <para>
/// In memory, the shape <see cref="SessionPrompts"/> has: the MCP listener runs in this
/// process (local ADR 0012), so the route and the screens share one instance, and a
/// restart forgets it, which reads as "not heard since the application started". A live
/// dispatcher calls again within one hold, so nothing is lost that persisting would keep.
/// It records no dispatcher's name: the route is called with the bearer token alone.
/// </para>
/// <para>
/// A call held open counts as contact for as long as it is held, because a dispatcher
/// waiting on the long poll is there right now; once it lets go, its arrival or its
/// reply is the last contact.
/// </para>
/// </summary>
public sealed class DispatcherContact
{
    /// <summary>How long without contact makes a dispatcher silent. Longer than the wait
    /// route's hold, so a dispatcher between two calls never reads as gone.</summary>
    public static readonly TimeSpan Threshold = TimeSpan.FromMinutes(2);

    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private DateTimeOffset? _last;
    private int _held;

    /// <param name="clock">The clock contact is read on; the system clock when null.</param>
    public DispatcherContact(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        StartedAt = _clock.GetUtcNow();
    }

    /// <summary>When this record began: the application's start, as far as contact goes.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>The last contact — now while a call is held open — or null when no
    /// dispatcher has called since the application started.</summary>
    public DateTimeOffset? LastHeardAt
    {
        get
        {
            lock (_gate)
            {
                return _held > 0 ? _clock.GetUtcNow() : _last;
            }
        }
    }

    /// <summary>Whether no dispatcher has been heard for more than <see cref="Threshold"/>,
    /// counted from <see cref="StartedAt"/> when none ever was, so the first call after a
    /// start has its hold's time to arrive before anyone is warned.</summary>
    public bool IsSilent
    {
        get
        {
            var now = _clock.GetUtcNow();

            lock (_gate)
            {
                return _held == 0 && now - (_last ?? StartedAt) > Threshold;
            }
        }
    }

    /// <summary>Records contact now.</summary>
    public void Heard()
    {
        var now = _clock.GetUtcNow();

        lock (_gate)
        {
            if (_last is not { } last || now > last) _last = now;
        }
    }

    /// <summary>Records contact now and keeps it current until the handle is disposed:
    /// one long-poll call held open. Disposing twice lets go once.</summary>
    public IDisposable Hold()
    {
        Heard();

        lock (_gate)
        {
            _held++;
        }

        return new Holding(this);
    }

    private void LetGo()
    {
        lock (_gate)
        {
            _held--;
        }
    }

    private sealed class Holding(DispatcherContact contact) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) contact.LetGo();
        }
    }
}
