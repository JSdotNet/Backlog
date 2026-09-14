using System.Collections;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// A read-only list that does not exist until something asks for it.
/// <para>
/// The knowledge read models are records whose expensive half is a list —
/// a context's documents, a document's parsed blocks. Handing them one of these
/// instead of a materialised list is what lets a panel be built from
/// <c>_meta/index.json</c> and still read Markdown for the one context or chapter
/// the reader opened: every caller keeps writing <c>context.Documents</c>, and
/// the parse happens on the first of those calls rather than on load.
/// </para>
/// <para>
/// Materialisation is once and for keeps — the panel enumerates the same list on
/// every re-render, and re-parsing per render would be worse than the eager load
/// this replaces.
/// </para>
/// </summary>
internal sealed class LazyKnowledgeList<T>(Func<IReadOnlyList<T>> materialize) : IReadOnlyList<T>
{
    private readonly Lazy<IReadOnlyList<T>> _items = new(materialize, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Whether the list has been asked for yet. Exists so a test can prove nothing was read.</summary>
    internal bool IsMaterialized => _items.IsValueCreated;

    public T this[int index] => _items.Value[index];

    public int Count => _items.Value.Count;

    public IEnumerator<T> GetEnumerator() => _items.Value.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
