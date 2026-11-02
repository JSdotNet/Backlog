namespace Backlog.Infrastructure.Devbook.Chapters;

/// <summary>
/// One Markdown file of a repository tree, as the one-time devbook import reads it:
/// its repository-relative path and its text, or why it could not be read.
/// </summary>
/// <param name="Path">Repository-relative, <c>/</c>-separated.</param>
/// <param name="Text">The file's text; null when it could not be read.</param>
/// <param name="Error">Why it could not be read; null when it was.</param>
public sealed record DevbookImportFile(string Path, string? Text, string? Error);

/// <summary>
/// The file half of the one-time devbook import: every Markdown file under the
/// folders the import names, read off a repository tree on disk — the default
/// branch's snapshot.
/// <para>
/// Here rather than beside the planner in the Devbook UI project, because a module
/// screen asks an adapter to open a file and never opens one itself
/// (<c>ModuleUiIoTests</c>). Reading only: nothing here writes, and nothing it is
/// given is a path outside <paramref name="root"/>'s tree.
/// </para>
/// </summary>
public static class DevbookImportFiles
{
    /// <summary>
    /// Every <c>*.md</c> file under each of <paramref name="folders"/>, at any depth,
    /// once each and in ordinal order of its path, leaving out any that passes
    /// through a directory named <paramref name="excludedDirectory"/>. A folder the
    /// tree does not have is no file; a file that cannot be read comes back with
    /// the reason instead of its text.
    /// </summary>
    /// <param name="root">The repository's root on disk.</param>
    /// <param name="folders">Repository-relative folders, <c>/</c>-separated.</param>
    /// <param name="excludedDirectory">A directory name nothing is read
    /// through — the changes' <c>archive</c>.</param>
    public static IReadOnlyList<DevbookImportFile> Read(string root, IEnumerable<string> folders, string? excludedDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(folders);

        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;

            var directory = System.IO.Path.Combine(root, folder);
            if (!Directory.Exists(directory)) continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.md", SearchOption.AllDirectories))
            {
                var relative = System.IO.Path.GetRelativePath(root, file).Replace('\\', '/');
                if (!PassesThrough(relative, excludedDirectory)) found.Add(relative);
            }
        }

        return [.. found.Select(path => ReadOne(root, path))];
    }

    private static DevbookImportFile ReadOne(string root, string path)
    {
        try
        {
            return new DevbookImportFile(path, File.ReadAllText(System.IO.Path.Combine(root, path)), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DevbookImportFile(path, null, ex.Message);
        }
    }

    private static bool PassesThrough(string path, string? directory)
    {
        if (string.IsNullOrEmpty(directory)) return false;

        var lastSeparator = path.LastIndexOf('/');
        return lastSeparator >= 0
            && path[..lastSeparator].Split('/').Contains(directory, StringComparer.OrdinalIgnoreCase);
    }
}
