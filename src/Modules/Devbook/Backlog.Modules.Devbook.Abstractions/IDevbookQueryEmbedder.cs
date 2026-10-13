namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// Turns a reader's question into the vector <see cref="IDevbookVectorSearch"/>
/// ranks against.
///
/// <para>A port of its own because <see cref="IDevbookVectorSearch"/> refuses to
/// reach a model — "the caller brings the vector" — and the search panel is that
/// caller. The panel asks this once per question, off its drawing path, and draws
/// the words answer while it waits.</para>
///
/// <para><see langword="null"/> is an ordinary answer: no model configured, the
/// network down, a deployment that refused. Search by meaning is additive, so a
/// question that could not be embedded is answered by its words alone and the
/// reader is told nothing went wrong, because nothing they asked for did.</para>
/// </summary>
public interface IDevbookQueryEmbedder
{
    /// <summary>The question as a vector from the model the databases are pinned
    /// to, or <see langword="null"/> when no model could be reached.</summary>
    Task<IReadOnlyList<float>?> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
