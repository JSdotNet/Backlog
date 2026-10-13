namespace Backlog.Infrastructure.Devbook;

/// <summary>
/// Which convention a folder handed to <see cref="DevbookDatabaseBuilder"/> is
/// read under.
/// </summary>
public enum DevbookCorpusKind
{
    /// <summary>A repository: its devbook folders under <c>.devbook/</c>, or at
    /// the root in the older layout.</summary>
    Repository,

    /// <summary>
    /// The Brain — the person's own notes, one folder outside every repository
    /// (local ADR 0026): the areas its <c>CONVENTIONS.md</c> declares instead of
    /// the devbook folders, the <c>para</c>, <c>repos</c> and <c>tasks</c>
    /// fields, and <c>repo:</c> links stored as edges nothing resolves.
    /// </summary>
    Brain
}
