using System.Collections.Concurrent;

namespace Backlog.Modules.Dashboard.Services;

/// <summary>
/// What the dashboard has already asked a provider, for as long as it stays open.
/// </summary>
/// <remarks>
/// <para>
/// Churn counting costs two or three GitHub calls per pull request, so a quarter
/// across five repositories is a few hundred calls. Without this, changing the
/// repository filter and changing it back would spend that budget twice for an
/// answer that cannot have moved.
/// </para>
/// <para>
/// The stored value is the <see cref="Task{TResult}"/> rather than its result, and
/// that is the point rather than a shortcut: two parts asking the same question at
/// the same moment — which is exactly what happens when the pane renders and every
/// part starts its own fetch — join one call instead of racing two.
/// </para>
/// <para>
/// A shared call runs under a token the entry owns, never under a caller's. The
/// callers waiting on it each observe their own token, and the entry's is cancelled
/// only when the last of them has left. This is what keeps a part that moves its
/// filter mid-load from stranding itself: it cancels its first fetch and starts a
/// second against the same entry, and a call bound to the first fetch's token would
/// hand the second a cancellation it never asked for — which the part, reasonably,
/// swallows as its own and stays on Loading with nothing left to wake it. Closing
/// the dashboard still stops the read, because then nobody is waiting.
/// </para>
/// <para>
/// No expiry and no clock. This is a session cache: it lives as long as the
/// dashboard is open and a refresh drops it. A staleness age would need a policy
/// nobody has asked for, and would make two parts able to disagree about what
/// "now" is.
/// </para>
/// <para>
/// A failed task is evicted rather than kept. Caching a failure would turn one
/// dropped connection into an unavailable part for the rest of the session, with
/// no way back but closing the dashboard.
/// </para>
/// </remarks>
internal sealed class InsightCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    internal async Task<T> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = _entries.GetOrAdd(key, _ => new Entry(async token => await factory(token).ConfigureAwait(false)));

            // An entry whose last waiter left between our lookup and our join is
            // already cancelled and on its way out; it is nobody's answer, so take
            // it out ourselves and start over on a fresh one.
            if (!entry.TryJoin(out var call))
            {
                Evict(key, entry);
                continue;
            }

            try
            {
                return (T)(await call.WaitAsync(cancellationToken).ConfigureAwait(false))!;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // This caller gave up; the call itself is still good for whoever else
                // is waiting on it, so it stays. Leaving, below, is what stops it if
                // nobody is.
                throw;
            }
            catch
            {
                Evict(key, entry);
                throw;
            }
            finally
            {
                if (entry.Leave())
                {
                    Evict(key, entry);
                }
            }
        }
    }

    /// <summary>Forgets every entry whose key starts with this prefix, which is
    /// how one part refreshes without discarding the others' work.</summary>
    internal void Invalidate(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        foreach (var key in _entries.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            _ = _entries.TryRemove(key, out _);
        }
    }

    internal void Clear() => _entries.Clear();

    /// <summary>Evicts by identity, so a retry that has already replaced this entry
    /// is not thrown away by a slower failure arriving after it.</summary>
    private void Evict(string key, Entry entry) =>
        _ = ((ICollection<KeyValuePair<string, Entry>>)_entries).Remove(new KeyValuePair<string, Entry>(key, entry));

    /// <summary>
    /// One shared call and the count of callers waiting on it. The call starts on the
    /// first join, under a token this entry owns, and is cancelled when the last
    /// waiter leaves before it has finished — after which the entry admits nobody, so
    /// a joiner arriving in that window is turned away to a fresh one rather than
    /// handed the cancelled task.
    /// </summary>
    private sealed class Entry
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _source = new();
        private readonly Lazy<Task<object?>> _task;
        private int _waiters;
        private bool _closed;

        public Entry(Func<CancellationToken, Task<object?>> call)
        {
            // Started lazily rather than in the constructor, because GetOrAdd may build
            // an entry that loses the race to another thread's and is never joined.
            // A Lazy rather than a null check under the gate, so the call's own
            // synchronous prefix runs outside the lock.
            _task = new Lazy<Task<object?>>(() => call(_source.Token));
        }

        public bool TryJoin(out Task<object?> task)
        {
            lock (_gate)
            {
                if (_closed)
                {
                    task = null!;
                    return false;
                }

                _waiters++;
            }

            task = _task.Value;
            return true;
        }

        /// <summary>Leaves the call; true when this was the last waiter on a call still
        /// in flight, which the leaver must then evict — the cancellation has already
        /// been requested by the time this returns.</summary>
        public bool Leave()
        {
            lock (_gate)
            {
                _waiters--;

                if (_waiters > 0 || (_task.IsValueCreated && _task.Value.IsCompleted))
                {
                    return false;
                }

                _closed = true;
            }

            _source.Cancel();
            return true;
        }
    }
}
