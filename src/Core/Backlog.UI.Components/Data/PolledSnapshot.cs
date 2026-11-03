using System.Diagnostics.CodeAnalysis;

namespace Backlog.UI.Components.Data;

/// <summary>
/// A keyed answer a screen keeps by reading it again on a timer, because it changes
/// where the screen cannot hear it: the last answer read, the timer, and the guard
/// that keeps a slow read from undoing a press the screen made meanwhile.
/// </summary>
/// <remarks>
/// <para>
/// The owner holds the source and decides when reading is worth it: the timer calls the
/// owner's <c>poll</c>, which checks its own conditions and then calls
/// <see cref="RefreshAsync"/>. A refresh keeps what it read only when it differs from
/// <see cref="Current"/>, and answers whether it did, so the owner redraws only on a
/// real change.
/// </para>
/// <para>
/// A press writes the answer itself — <see cref="Put"/> or <see cref="ReadBackAsync"/> —
/// so it first calls <see cref="Supersede"/>: a read that set out before that call
/// lands after it and would otherwise put back the answer the press replaced.
/// </para>
/// <para>
/// A failed read keeps the last answer; the next tick tries again. After
/// <see cref="Dispose"/> nothing is kept and the timer is stopped.
/// </para>
/// </remarks>
/// <typeparam name="TKey">What an answer is looked up by.</typeparam>
/// <typeparam name="TValue">One answer; compared by its own equality.</typeparam>
public sealed class PolledSnapshot<TKey, TValue> : IDisposable
    where TKey : notnull
{
    private readonly Func<Task<IReadOnlyDictionary<TKey, TValue>>> _read;
    private readonly IEqualityComparer<TKey>? _keys;
    private readonly ITimer? _poll;
    private IReadOnlyDictionary<TKey, TValue> _current;
    private volatile bool _disposed;
    private int _version;

    /// <param name="read">Reads the whole answer from its source.</param>
    /// <param name="poll">What each tick runs, or null for no timer — the owner has no
    /// source to read.</param>
    /// <param name="interval">How long between ticks.</param>
    /// <param name="keys">How keys compare; the key type's default when null.</param>
    /// <param name="clock">What the timer waits on; the system clock when null.</param>
    public PolledSnapshot(
        Func<Task<IReadOnlyDictionary<TKey, TValue>>> read,
        Func<Task>? poll,
        TimeSpan interval,
        IEqualityComparer<TKey>? keys = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(read);

        _read = read;
        _keys = keys;
        _current = new Dictionary<TKey, TValue>(keys);

        if (poll is not null)
        {
            _poll = (clock ?? TimeProvider.System).CreateTimer(_ => _ = poll(), null, interval, interval);
        }
    }

    /// <summary>The last answer kept.</summary>
    public IReadOnlyDictionary<TKey, TValue> Current => _current;

    /// <summary>Whether <see cref="Dispose"/> has been called.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Moves on with every <see cref="Supersede"/>; a read that set out at an
    /// older value is not kept.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>Looks <paramref name="key"/> up in <see cref="Current"/>.</summary>
    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value) => _current.TryGetValue(key, out value);

    /// <summary>A press is about to change the answer: no read already on its way is kept.</summary>
    public void Supersede() => Interlocked.Increment(ref _version);

    /// <summary>Sets one answer, as a press that knows it does.</summary>
    public void Put(TKey key, TValue value) =>
        _current = new Dictionary<TKey, TValue>(_current, _keys) { [key] = value };

    /// <summary>Reads the answer and keeps it when it differs from <see cref="Current"/>,
    /// no <see cref="Supersede"/> happened since <paramref name="setOutAt"/> (the version
    /// on entry when null), and <paramref name="pressInFlight"/>, when given, answers
    /// false once the read is in. Answers whether the kept answer changed; false on a
    /// failed read or after disposal.</summary>
    public async Task<bool> RefreshAsync(int? setOutAt = null, Func<bool>? pressInFlight = null)
    {
        var version = setOutAt ?? Version;

        IReadOnlyDictionary<TKey, TValue> read;
        try
        {
            read = await _read();
        }
        catch (Exception)
        {
            return false;
        }

        if (_disposed || pressInFlight?.Invoke() == true || version != Version || Same(read, _current)) return false;

        _current = new Dictionary<TKey, TValue>(read, _keys);
        return true;
    }

    /// <summary>Reads the answer and keeps it whatever it is, as a press that wants to
    /// show where its source moved. A failed read keeps the last answer.</summary>
    public async Task ReadBackAsync()
    {
        if (_disposed) return;

        try
        {
            _current = new Dictionary<TKey, TValue>(await _read(), _keys);
        }
        catch (Exception)
        {
            // The poll will try again.
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _poll?.Dispose();
    }

    private static bool Same(IReadOnlyDictionary<TKey, TValue> a, IReadOnlyDictionary<TKey, TValue> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) && EqualityComparer<TValue>.Default.Equals(other, pair.Value));
}
