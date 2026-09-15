using System.Text.RegularExpressions;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// A branch's index, answering as if it were the disk.
/// <para>
/// This is what lets the menu be drawn before a chapter has been fetched: it
/// enumerates the paths the commit contains, rooted at the snapshot's tree
/// folder, so every full path it hands out is exactly where the file will be
/// once <see cref="IDevbookSnapshotCache.EnsureAsync"/> has put it. The menu
/// walks it with the same calls it walks a clone with, and does not know which
/// it is walking.
/// </para>
/// </summary>
public sealed class DevbookSnapshotFileTree : IDevbookFileTree
{
    private readonly string _root;
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _childDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _childFiles = new(StringComparer.OrdinalIgnoreCase);

    public DevbookSnapshotFileTree(string root, IEnumerable<DevbookSnapshotEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(entries);

        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _directories.Add(string.Empty);

        foreach (var entry in entries)
        {
            var path = DevbookSnapshotSelection.Normalize(entry.Path).TrimEnd('/');
            if (path.Length == 0) continue;

            // Every ancestor is a directory whether or not the listing named it
            // — GitHub's does, but an index is a file somebody could hand-edit.
            var parent = Parent(path);
            while (parent.Length > 0 && _directories.Add(parent))
            {
                Children(_childDirectories, Parent(parent)).Add(parent);
                parent = Parent(parent);
            }

            if (entry.IsDirectory)
            {
                if (_directories.Add(path)) Children(_childDirectories, Parent(path)).Add(path);
            }
            else if (_files.Add(path))
            {
                Children(_childFiles, Parent(path)).Add(path);
            }
        }
    }

    public bool DirectoryExists(string fullPath) =>
        TryRelative(fullPath, out var relative) && _directories.Contains(relative);

    public bool FileExists(string fullPath) =>
        TryRelative(fullPath, out var relative) && _files.Contains(relative);

    public IEnumerable<string> EnumerateDirectories(string fullPath)
    {
        if (!TryRelative(fullPath, out var relative) || !_directories.Contains(relative))
        {
            throw new DirectoryNotFoundException($"Could not find a part of the path '{fullPath}'.");
        }

        return _childDirectories.TryGetValue(relative, out var children)
            ? children.Select(Full)
            : [];
    }

    public IEnumerable<string> EnumerateFiles(string fullPath, string searchPattern, bool recursive = false)
    {
        if (!TryRelative(fullPath, out var relative) || !_directories.Contains(relative))
        {
            throw new DirectoryNotFoundException($"Could not find a part of the path '{fullPath}'.");
        }

        var pattern = Pattern(searchPattern);

        var candidates = recursive
            ? _files.Where(file => relative.Length == 0 || file.StartsWith(relative + "/", StringComparison.OrdinalIgnoreCase))
            : _childFiles.TryGetValue(relative, out var children) ? children : [];

        return candidates
            .Where(file => pattern.IsMatch(Name(file)))
            .Select(Full);
    }

    private bool TryRelative(string fullPath, out string relative)
    {
        relative = string.Empty;
        if (string.IsNullOrWhiteSpace(fullPath)) return false;

        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (string.Equals(full, _root, StringComparison.OrdinalIgnoreCase)) return true;
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;

        relative = full[(_root.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
        return true;
    }

    private string Full(string relative) =>
        relative.Length == 0 ? _root : Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string Parent(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? string.Empty : path[..separator];
    }

    private static string Name(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? path : path[(separator + 1)..];
    }

    private static List<string> Children(Dictionary<string, List<string>> map, string parent)
    {
        if (!map.TryGetValue(parent, out var children))
        {
            children = [];
            map[parent] = children;
        }

        return children;
    }

    /// <summary><c>*</c> and <c>?</c>, as <see cref="Directory.EnumerateFiles(string, string)"/>
    /// reads them, over the file name alone; everything else literal.</summary>
    private static Regex Pattern(string searchPattern)
    {
        var pattern = string.IsNullOrWhiteSpace(searchPattern) ? "*" : searchPattern.Trim();

        return new Regex(
            "^" + Regex.Escape(pattern).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
