namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The Brain as the Devbook pane reads it (local ADR 0026): one local folder,
/// resolved under <see cref="BrainConventions.FolderKey"/> for the unscoped
/// question, with the areas its <c>CONVENTIONS.md</c> declares.
///
/// <para>A port of its own rather than a second <see cref="IDevbookFolderSource"/>
/// registration: the repository source is the one every search, reference,
/// annotation and Archify reader resolves, and registering the Brain under that
/// type would replace it. The pane asks this one for the Brain and the other for
/// a repository, and nothing else has to know there are two.</para>
///
/// <para><see cref="IDevbookFolderSource.Changed"/> is raised when the Brain
/// setting moves and after the Brain's database was rebuilt, so an open pane
/// reloads without a restart.</para>
/// </summary>
public interface IBrainFolderSource : IDevbookFolderSource
{
    /// <summary>The Brain's absolute folder, or null while none is set or the
    /// setting is not a usable path. Set does not mean present: the folder may
    /// be missing, which <see cref="IDevbookFolderSource.Resolve"/> says in
    /// words.</summary>
    string? BrainRoot { get; }

    /// <summary>Whether <see cref="BrainRoot"/> is set and the folder is there now:
    /// one look at the disk, no read of a note — the cheap question a screen asks
    /// to notice a folder that went away without raising
    /// <see cref="IDevbookFolderSource.Changed"/>.</summary>
    bool BrainFolderPresent();

    /// <summary>The areas the Brain shows, in order: what its
    /// <c>CONVENTIONS.md</c> declares, or the PARA set.</summary>
    IReadOnlyList<BrainArea> Areas();

    /// <summary>The OneDrive conflict pairs in the Brain
    /// (<see cref="BrainConflictCopies"/>), read only — neither file is ever
    /// changed. Empty while no folder is set or the folder is missing.</summary>
    IReadOnlyList<BrainConflictCopy> ConflictCopies();
}
