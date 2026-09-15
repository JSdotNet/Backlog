namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The shape of a resolved knowledge folder — which directories and files it
/// holds — asked of whoever knows, rather than of the disk.
/// <para>
/// For a local folder that is the disk, and <see cref="DevbookDiskFileTree"/>
/// simply forwards. For a branch it is the branch's listing, which the source
/// holds before a single chapter has been fetched: the menu is built from the
/// listing and the files arrive when somebody opens them. Only listing goes
/// through here. Reading a file's text is still <c>File.ReadAllText</c> on the
/// path this tree named, after <see cref="IDevbookFolderSource.PrepareContentAsync"/>
/// has put it there — a tree that also served content would be a second file
/// system, and every reader in the module would have to move onto it.
/// </para>
/// <para>
/// Paths in and out are full paths, exactly as <see cref="Directory"/> would
/// give and take them, so a caller that enumerated the disk yesterday
/// enumerates this today with nothing else changed.
/// </para>
/// </summary>
public interface IDevbookFileTree
{
    bool DirectoryExists(string fullPath);

    bool FileExists(string fullPath);

    /// <summary>The immediate subdirectories, as full paths, in no particular
    /// order.</summary>
    IEnumerable<string> EnumerateDirectories(string fullPath);

    /// <summary>The files in a directory matching a <c>*</c>/<c>?</c> pattern
    /// such as <c>*.md</c>, as full paths, in no particular order — this level
    /// only, or every level below when <paramref name="recursive"/>.</summary>
    IEnumerable<string> EnumerateFiles(string fullPath, string searchPattern, bool recursive = false);
}

/// <summary>The disk, as an <see cref="IDevbookFileTree"/>. What every local
/// folder answers with, and what every source answered with before a branch
/// could be listed without being fetched.</summary>
public sealed class DevbookDiskFileTree : IDevbookFileTree
{
    public static DevbookDiskFileTree Instance { get; } = new();

    private DevbookDiskFileTree()
    {
    }

    public bool DirectoryExists(string fullPath) => Directory.Exists(fullPath);

    public bool FileExists(string fullPath) => File.Exists(fullPath);

    public IEnumerable<string> EnumerateDirectories(string fullPath) => Directory.EnumerateDirectories(fullPath);

    public IEnumerable<string> EnumerateFiles(string fullPath, string searchPattern, bool recursive = false) =>
        Directory.EnumerateFiles(fullPath, searchPattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
}
