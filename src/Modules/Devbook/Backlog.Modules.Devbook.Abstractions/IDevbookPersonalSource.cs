namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// One devbook beside the repositories': unscoped, always editable, kept in one
/// local folder outside every repository. Another context answers it (local ADR
/// 0040 §2); Devbook only reads it, through this port, and shows its top folders
/// as tabs.
///
/// <para>Registered on its own — never as a second <see cref="IDevbookFolderSource"/>,
/// which would replace the repository source every search, reference and
/// annotation reader resolves. The pane asks this one for the personal devbook
/// and the other for a repository.</para>
///
/// <para><see cref="IDevbookFolderSource.Changed"/> is raised when the folder
/// setting moves and after the devbook's database was rebuilt, so an open pane
/// reloads without a restart.</para>
/// </summary>
public interface IDevbookPersonalSource : IDevbookFolderSource
{
    /// <summary>The key <see cref="IDevbookFolderSource.Resolve"/> answers this
    /// devbook under.</summary>
    string FolderKey { get; }

    /// <summary>The devbook's absolute folder, or null while none is set or the
    /// setting is not a usable path. Set does not mean present: the folder may be
    /// missing, which <see cref="IDevbookFolderSource.Resolve"/> says in
    /// words.</summary>
    string? Root { get; }

    /// <summary>Whether <see cref="Root"/> is set and the folder is there now: one
    /// look at the disk, no read of a chapter.</summary>
    bool RootPresent();

    /// <summary>The tabs the pane shows, in order. Walks the folder's names, so a
    /// screen asks it off its render thread.</summary>
    IReadOnlyList<DevbookTab> Tabs();

    /// <summary>What <b>Show hidden</b> adds after <see cref="Tabs"/>. Empty while
    /// there are none, no folder is set or the folder is missing.</summary>
    IReadOnlyList<DevbookTab> HiddenTabs();

    /// <summary>The sync conflict copies in the folder, read only — neither file is
    /// ever changed. A folder walk; empty while no folder is set or the folder is
    /// missing.</summary>
    IReadOnlyList<DevbookConflictPair> ConflictPairs();
}

/// <summary>One tab of a <see cref="IDevbookPersonalSource"/>: a top folder and what
/// it is labelled.</summary>
/// <param name="Folder">The folder, relative to the devbook's root.</param>
/// <param name="Label">What the tab reads.</param>
/// <param name="NewestFirst">Whether the tab lists its chapters newest first rather
/// than in reading order.</param>
public sealed record DevbookTab(string Folder, string Label, bool NewestFirst = false);

/// <summary>A sync conflict pair in a <see cref="IDevbookPersonalSource"/>: the
/// original file and the copy beside it, both relative to the devbook's
/// root.</summary>
/// <param name="Original">The file the copy conflicts with.</param>
/// <param name="Copy">The conflict copy.</param>
public sealed record DevbookConflictPair(string Original, string Copy);
