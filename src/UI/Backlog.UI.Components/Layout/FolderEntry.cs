namespace Backlog.UI.Components.Layout;

/// <summary>What a row in a folder is. Folders may hold more of these; files may
/// not, and a caller that gives a file children is describing something this
/// component has no way to draw.</summary>
public enum FolderEntryKind
{
    Folder,
    File
}

/// <summary>
/// One thing inside a folder, as <see cref="FolderView"/> needs to know it.
/// <para>
/// This is the shape the desktop's knowledge menu grew into, with the vocabulary
/// taken back out. That menu knew about areas, repository aliases and which
/// chapter of arc42 sorts before which — none of which a folder has. What is left
/// is what any folder anywhere has: a path, a name, whether it is a folder, what
/// is inside it, and whether it can be read at all.
/// </para>
/// <para>
/// A caller keeps its own richer type and maps onto this, the same bargain
/// <see cref="Menus.TreeNode"/> offers: <see cref="Path"/> is how it finds the
/// original again, so it must be unique within one <see cref="FolderView"/>.
/// </para>
/// </summary>
/// <param name="Path">Unique within the folder, and what a selection reports.
/// Usually the path relative to the folder's root.</param>
/// <param name="Name">What the row shows.</param>
/// <param name="Children">What is inside, for a folder. Null and empty mean the
/// same thing to the view; they mean different things to a reader, so a caller
/// that has not looked yet should say so with <paramref name="Message"/>.</param>
/// <param name="SizeInBytes">Shown beside a file's name. Null when the caller
/// does not know it — better an absent fact than a fabricated zero, the same rule
/// <see cref="FileView"/> follows.</param>
/// <param name="Available">False for something that is there but cannot be
/// opened — no permission, a broken link, a folder that failed to enumerate. The
/// row still shows, disabled, because a reader who cannot see it will look for
/// it.</param>
/// <param name="Message">Why it is unavailable, or anything else worth saying on
/// hover.</param>
public sealed record FolderEntry(
    string Path,
    string Name,
    FolderEntryKind Kind = FolderEntryKind.File,
    IReadOnlyList<FolderEntry>? Children = null,
    long? SizeInBytes = null,
    bool Available = true,
    string? Message = null)
{
    /// <summary>What is inside, never null — so a caller can walk without asking
    /// first.</summary>
    public IReadOnlyList<FolderEntry> Items => Children ?? [];

    public bool IsFolder => Kind is FolderEntryKind.Folder;

    /// <summary>A folder, with what is in it.</summary>
    public static FolderEntry Folder(string path, string name, params FolderEntry[] children) =>
        new(path, name, FolderEntryKind.Folder, children);

    /// <summary>A file.</summary>
    public static FolderEntry File(string path, string name, long? sizeInBytes = null) =>
        new(path, name, FolderEntryKind.File, null, sizeInBytes);

    /// <summary>Everything under this entry, including itself, depth first — the
    /// order it is written on screen.</summary>
    public IEnumerable<FolderEntry> Flatten()
    {
        yield return this;

        foreach (var descendant in Items.SelectMany(child => child.Flatten()))
        {
            yield return descendant;
        }
    }
}
