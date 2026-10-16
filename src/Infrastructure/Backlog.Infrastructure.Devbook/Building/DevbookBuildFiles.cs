using System.Text;

using Backlog.Modules.Devbook.Abstractions;

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
    public static IReadOnlyList<string> Markdown(string repositoryRoot, string folder, bool skipGenerated, bool placeholders = false)
    {
        var found = new List<string>();
        Walk(folder);
        found.Sort(StringComparer.Ordinal);
        return found;

        void Walk(string relative)
        {
            foreach (var (name, isDirectory) in Entries(repositoryRoot, relative, placeholders))
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

    /// <summary>Every demo — <c>demo.html</c>, <c>&lt;page&gt;.demo.html</c> — under
    /// <paramref name="folder"/>, outside the generated directories, as the Node
    /// writer's <c>collectDemos</c> finds them.</summary>
    public static IReadOnlyList<string> Demos(string repositoryRoot, string folder, bool placeholders = false)
    {
        var found = new List<string>();
        Walk(folder);
        found.Sort(StringComparer.Ordinal);
        return found;

        void Walk(string relative)
        {
            foreach (var (name, isDirectory) in Entries(repositoryRoot, relative, placeholders))
            {
                var child = $"{relative}/{name}";
                if (isDirectory)
                {
                    if (!SkippedDirectories.Contains(name)) Walk(child);
                }
                else if (DevbookReadingConvention.IsDemo(name))
                {
                    found.Add(child);
                }
            }
        }
    }

    /// <summary>Every <c>_archify/index.json</c> under <paramref name="folder"/>,
    /// whether or not the file is there — a folder with an <c>_archify</c>
    /// directory and no readable index is a problem to record, not to skip.</summary>
    public static IReadOnlyList<string> ArchifyIndexes(string repositoryRoot, string folder, bool placeholders = false)
    {
        var found = new List<string>();
        Walk(folder);
        found.Sort(StringComparer.Ordinal);
        return found;

        void Walk(string relative)
        {
            foreach (var (name, isDirectory) in Entries(repositoryRoot, relative, placeholders))
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
    /// either kind — a symbolic link, a junction — is neither, as it is to Node's
    /// <c>Dirent</c>. A directory that cannot be read has no entries.
    /// <para>
    /// With <paramref name="placeholders"/> — a Brain's walk — a link is told by
    /// its target rather than by the reparse-point attribute, because a synced
    /// folder's placeholders carry that attribute too: under OneDrive's Files
    /// On-Demand every file and folder is a cloud-file reparse point, cloud only
    /// or not, and skipping them all would leave a Brain kept in OneDrive with
    /// nothing to index. A repository's walk keeps skipping every reparse point,
    /// as Node's <c>Dirent</c> does, so the two writers still agree on a clone
    /// that happens to sit in a synced or virtualized folder.
    /// </para>
    /// </summary>
    public static IEnumerable<(string Name, bool IsDirectory)> Entries(string repositoryRoot, string relative, bool placeholders = false)
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
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint) && (!placeholders || IsLink(entry))) continue;
            yield return (entry.Name, entry is DirectoryInfo);
        }
    }

    /// <summary>
    /// Whether the file is a cloud-only placeholder: listed on this machine but
    /// with its content still in the cloud, so reading it would download it.
    /// <para>
    /// Such a file is indexed by its name alone — a node, an outline row — and
    /// not read: a build that opened every placeholder of a synced Brain would
    /// download the whole folder behind the person's back. Once the sync client
    /// hydrates it, the attributes change, the inputs fingerprint with them
    /// (<see cref="DevbookDatabaseBuilder.IsCurrent(string, string, DevbookCorpusKind)"/>),
    /// and the next build reads its <c>meta</c> and its text.
    /// </para>
    /// </summary>
    public static bool IsCloudOnly(string repositoryRoot, string relative)
    {
        try
        {
            return IsCloudOnly(File.GetAttributes(Absolute(repositoryRoot, relative)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Whether <paramref name="attributes"/> mark a cloud-only file:
    /// recall on data access or on open (the Windows cloud-files API's
    /// placeholders), or offline (the older marker the same clients set).</summary>
    public static bool IsCloudOnly(FileAttributes attributes) =>
        (attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0;

    /// <summary>A file's text as <see cref="ReadText"/> reads it, or
    /// <see langword="null"/> for a cloud-only file (<see cref="IsCloudOnly(string, string)"/>),
    /// which is not read.</summary>
    public static string? ReadHydratedText(string repositoryRoot, string relative) =>
        IsCloudOnly(repositoryRoot, relative) ? null : ReadText(repositoryRoot, relative);

    /// <summary><c>FILE_ATTRIBUTE_RECALL_ON_OPEN</c>, which
    /// <see cref="FileAttributes"/> does not name.</summary>
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;

    /// <summary><c>FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS</c>, which
    /// <see cref="FileAttributes"/> does not name.</summary>
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    /// <summary>Whether a reparse point is a link. When its target cannot be read
    /// — a placeholder while its sync client is not running — a file is kept, and
    /// only a directory, the one kind that could loop, is treated as a link.</summary>
    private static bool IsLink(FileSystemInfo entry)
    {
        try
        {
            return entry.LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return entry is DirectoryInfo;
        }
    }

    /// <summary>A file's text as <c>readFile(path, 'utf8')</c> yields it: a byte
    /// order mark is kept as U+FEFF rather than stripped.</summary>
    public static string ReadText(string repositoryRoot, string relative) =>
        Encoding.UTF8.GetString(File.ReadAllBytes(Absolute(repositoryRoot, relative)));

    public static string Absolute(string repositoryRoot, string relative) =>
        Path.Combine(repositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar));
}
