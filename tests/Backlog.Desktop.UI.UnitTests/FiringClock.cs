using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that also counts the timer callbacks it has
/// fired.
/// <para>
/// A test that asserts nothing happened after disposal cannot wait for the
/// absence of a write: the callback a moved clock fires may start store or file
/// I/O that lands only after the assertion has passed. The callback itself runs
/// inside <see cref="Advance"/>, on the thread that moved the clock, so the count
/// is settled the moment <see cref="Advance"/> returns — and a callback that
/// should have been disarmed shows up in it whether or not its write has landed.
/// </para>
/// </summary>
internal sealed class FiringClock : TimeProvider
{
    private readonly FakeTimeProvider _inner = new();
    private int _fired;

    /// <summary>Timer callbacks this clock has run so far, of every timer and
    /// every delay made on it.</summary>
    public int Fired => Volatile.Read(ref _fired);

    public void Advance(TimeSpan delta) => _inner.Advance(delta);

    public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();

    public override long GetTimestamp() => _inner.GetTimestamp();

    public override long TimestampFrequency => _inner.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => _inner.LocalTimeZone;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        _inner.CreateTimer(
            callbackState =>
            {
                Interlocked.Increment(ref _fired);
                callback(callbackState);
            },
            state,
            dueTime,
            period);
}
