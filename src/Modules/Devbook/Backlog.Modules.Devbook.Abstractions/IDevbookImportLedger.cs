namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// Which repositories the one-time devbook import has finished for on this device.
/// <para>
/// The import reads a repository's default branch once and writes the state and
/// the open notes it finds into Backlog's own stores. It is marked here only when
/// it ran to the end; a default branch that could not be read leaves no mark, so
/// the import tries again on the next start. A repository marked here is not read
/// again — not even when its devbook changes later, because from then on the
/// stores, not the Markdown, are where its state and notes are kept.
/// </para>
/// <para>
/// Not replicated: each device imports for itself, and the derived ids make the
/// copies one. Keyed by repository alias, folded as every devbook store folds it.
/// </para>
/// </summary>
public interface IDevbookImportLedger
{
    /// <summary>Whether the import has finished for this repository.</summary>
    bool HasImported(string repositoryAlias);

    /// <summary>Records that the import has finished for this repository.</summary>
    void MarkImported(string repositoryAlias, DateTimeOffset at);
}
