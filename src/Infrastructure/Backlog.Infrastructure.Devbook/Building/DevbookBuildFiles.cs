using System.Text;

namespace Backlog.Infrastructure.Devbook.Building;

/// <summary>
/// The files a build reads, found the way the Node writer finds them, and read
/// the way Node reads them.
///
/// <para>Paths come back repository-relative with forward slashes and sorted by
/// UTF-16 code unit — JavaScript's default <c>sort()</c> — because row order and
/// outline order are compared against the Node writer's.</para>
/// </summary>
internal static class DevbookBuildFiles
{
    /// <summary>Generated output and vendored trees: they hold no authored
    /// Markdown, and the chapter walk skips them.</summary>
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.Ordinal) { "_meta", "_archify", "node_modules", ".git" };

    /// <summary>
    /// Every <c>.md</c> file under <paramref name="folder"/>. The graph walks
    /// every directory (<paramref name="skipGenerated"/> false, as the generator's
    /// <c>collectMarkdown</c> does); the chapter rows skip generated ones.
    /// </summary>
    public static IReadOnlyList<string> Markdown(string repositoryRoot, string folder, bool skipGenerated)
    {
        var found = new List<string>();
        Walk(folder);
        found.Sort(StringComparer.Ordinal);
        return found;

        void Walk(string relative)
        {
            foreach (var (name, isDirectory) in Entries(repositoryRoot, relative))
            {
                var child = $"{relative}/{name}";
                if (isDirectory)
                {
                    if (skipGenerated && SkippedDirectories.Contains(name)) continue;
                    Walk(child);
                }
                else if (name.EndsWith(".md", StringComparison.Ordinal))
                {
                    found.Add(child);
                }
            }
        }
    }

    /// <summary>Every <c>_archify/index.json</c> under <paramref name="folder"/>,
    /// whether or not the file is there — a folder with an <c>_archify</c>
    /// directory and no readable index is a problem to record, not to skip.</summary>
    public static IReadOnlyList<string> ArchifyIndexes(string repositoryRoot, string folder)
    {
        var found = new List<string>();
        Walk(folder);
        found.Sort(StringComparer.Ordinal);
        return found;

        void Walk(string relative)
        {
            foreach (var (name, isDirectory) in Entries(repositoryRoot, relative))
            {
                if (!isDirectory) continue;
                var child = $"{relative}/{name}";
                if (name == "_archify") found.Add($"{child}/index.json");
                else if (!SkippedDirectories.Contains(name)) Walk(child);
            }
        }
    }

    /// <summary>
    /// One directory's entries: plain files and plain directories. A link of
    /// either kind is neither, as it is to Node's <c>Dirent</c>. A directory that
    /// cannot be read has no entries.
    /// </summary>
    public static IEnumerable<(string Name, bool IsDirectory)> Entries(string repositoryRoot, string relative)
    {
        FileSystemInfo[] entries;
        try
        {
            entries = new DirectoryInfo(Absolute(repositoryRoot, relative)).GetFileSystemInfos();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            yield break;
        }

        foreach (var entry in entries)
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            yield return (entry.Name, entry is DirectoryInfo);
        }
    }

    /// <summary>A file's text as <c>readFile(path, 'utf8')</c> yields it: a byte
    /// order mark is kept as U+FEFF rather than stripped.</summary>
    public static string ReadText(string repositoryRoot, string relative) =>
        Encoding.UTF8.GetString(File.ReadAllBytes(Absolute(repositoryRoot, relative)));

    public static string Absolute(string repositoryRoot, string relative) =>
        Path.Combine(repositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar));
}
