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
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _entries = new(StringComparer.Ordinal);

    internal async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        var entry = _entries.GetOrAdd(
            key,
            _ => new Lazy<Task<object?>>(async () => await factory().ConfigureAwait(false)));

        try
        {
            return (T)(await entry.Value.ConfigureAwait(false))!;
        }
        catch
        {
            // Evict by identity, so a retry that has already replaced this entry
            // is not thrown away by a slower failure arriving after it.
            _ = ((ICollection<KeyValuePair<string, Lazy<Task<object?>>>>)_entries)
                .Remove(new KeyValuePair<string, Lazy<Task<object?>>>(key, entry));
            throw;
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
}
