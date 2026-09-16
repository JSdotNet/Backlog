using System.Collections.Concurrent;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// A parse remembered for as long as the file it came from stays the same.
/// <para>
/// Every knowledge store parses a folder into a read model, and every one of
/// them used to do it again on each load — which, because a section panel is
/// disposed whenever its tab is left, meant every return to Architecture parsed
/// thirty-nine chapters to show the one already open, and every save debounce
/// parsed them again to redraw the list beside it. The Markdown had not changed;
/// only the component holding the result had gone.
/// </para>
/// <para>
/// So the result is kept here, by the store, and handed back while the file
/// still answers to the same size and modification time — the same two facts
/// <c>DevbookFileState</c> trusts before it spends a hash, and the same trade: a
/// touched file is re-read whether or not a byte moved. A save therefore costs
/// one file's parse rather than the folder's, and a tab switch costs none.
/// </para>
/// <para>
/// <see cref="Clear"/> is wired to the folder source's <c>Changed</c>, because a
/// folder that moved or was replaced underneath the app is a different set of
/// files under the same names, and a stamp is about a name. Values must be
/// immutable: the same instance is handed to every caller until the file moves.
/// </para>
/// </summary>
internal sealed class DevbookFileCache<T> where T : class
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The value for <paramref name="key"/>, produced by <paramref name="produce"/>
    /// when nothing is remembered or when <paramref name="stampedFile"/> has
    /// changed since it was.
    /// <para>
    /// The stamp is taken <em>before</em> the value is produced. A write landing
    /// between the two leaves the remembered stamp older than the file, which is
    /// a re-parse on the next ask rather than a stale answer served forever — the
    /// harmless side of the race.
    /// </para>
    /// <para>
    /// A file that cannot be stamped — gone, or unreadable — is produced and not
    /// remembered, so a caller sees exactly the exception or fallback it always
    /// did.
    /// </para>
    /// </summary>
    public T GetOrAdd(string key, string stampedFile, Func<T> produce)
    {
        ArgumentNullException.ThrowIfNull(produce);

        var stamp = Stamp(stampedFile);
        if (stamp is null) return produce();

        if (_entries.TryGetValue(key, out var hit) && hit.Stamp == stamp.Value) return hit.Value;

        var value = produce();
        _entries[key] = new Entry(stamp.Value, value);
        return value;
    }

    /// <summary>The value for a file, keyed and stamped by that file.</summary>
    public T GetOrAdd(string fullPath, Func<T> produce) => GetOrAdd(fullPath, fullPath, produce);

    /// <summary>Whether a current value is remembered for <paramref name="key"/>.
    /// Exists so a test can prove a load served from memory.</summary>
    internal bool Holds(string key, string stampedFile) =>
        _entries.TryGetValue(key, out var hit) && Stamp(stampedFile) is { } stamp && hit.Stamp == stamp;

    public void Clear() => _entries.Clear();

    private static (long Length, long WriteTicks)? Stamp(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? (file.Length, file.LastWriteTimeUtc.Ticks) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private sealed record Entry((long Length, long WriteTicks) Stamp, T Value);
}
