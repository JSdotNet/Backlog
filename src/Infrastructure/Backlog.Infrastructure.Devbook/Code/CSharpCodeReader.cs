using System.Collections.Concurrent;

namespace Backlog.Infrastructure.Devbook.Code;

/// <summary>
/// Reads the Code level of a C4 component from its C# source, on demand (local
/// ADR 0037 §3).
/// <para>
/// Every <c>*.cs</c> under the folder is parsed with
/// <c>CSharpSyntaxTree.ParseText</c> and nothing more: no compilation, no
/// project file, no restore, so a branch snapshot or a repository the app never
/// builds reads the same as the clone it does. What only a semantic model could
/// answer — a member's resolved type, which overload a call binds to — is not
/// in the result; <see cref="CSharpSyntaxModelBuilder"/> says what is.
/// </para>
/// <para>
/// <c>bin/</c> and <c>obj/</c> are skipped at any depth: what a build generates
/// there is not the component's code, and walking them would cost more than the
/// source does.
/// </para>
/// <para>
/// A read is remembered per folder while every file in it keeps its path, size
/// and modification time — the stamp <see cref="DevbookFileCache{T}"/> trusts
/// for one file, taken over the folder. Re-opening the Code view of an unchanged
/// component costs one directory walk; a file added, edited or removed is a new
/// parse of the folder. The stamp is taken before the parse, so a write landing
/// during one is a re-parse on the next ask rather than a stale answer.
/// </para>
/// </summary>
public sealed class CSharpCodeReader
{
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj" };

    // A link or junction is not followed: one can loop, and one can point
    // outside the repository the folder was checked against.
    private static readonly EnumerationOptions Listing = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
    };

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The types under <paramref name="folder"/>, read from the source as it is
    /// now, or as it was at the last read when nothing has moved since.
    /// </summary>
    /// <param name="repositoryRoot">The repository's root folder on disk.</param>
    /// <param name="folder">The folder to read, relative to
    /// <paramref name="repositoryRoot"/>; <c>/</c> or <c>\</c> separated.</param>
    /// <exception cref="ArgumentException"><paramref name="folder"/> resolves
    /// outside <paramref name="repositoryRoot"/>.</exception>
    public CodeModel Read(string repositoryRoot, string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(folder);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var fullFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(root, folder)));
        if (!IsWithin(root, fullFolder))
        {
            throw new ArgumentException($"'{folder}' is not inside the repository.", nameof(folder));
        }

        var relativeFolder = ToRelative(root, fullFolder);
        if (!Directory.Exists(fullFolder)) return CodeModel.Empty(relativeFolder);

        var files = SourceFiles(root, fullFolder);
        if (_entries.TryGetValue(fullFolder, out var hit) && hit.Files.SequenceEqual(files)) return hit.Model;

        var model = CSharpSyntaxModelBuilder.Build(relativeFolder, files.Select(file => (file.RelativePath, Path.Combine(root, file.RelativePath))), out var complete);

        // A file that could not be read this time (an editor holding it mid-save)
        // keeps its stamp, so remembering this model would hide it until the next
        // edit. Not remembered, the next read tries it again.
        if (complete) _entries[fullFolder] = new Entry(files, model);
        else _entries.TryRemove(fullFolder, out _);
        return model;
    }

    /// <summary>Forgets every folder read, for a repository that moved or was
    /// replaced underneath the app.</summary>
    public void Clear() => _entries.Clear();

    private static List<SourceFile> SourceFiles(string root, string folder)
    {
        var files = new List<SourceFile>();
        var pending = new Stack<string>();
        pending.Push(folder);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(current, "*", Listing))
                {
                    if (!SkippedFolders.Contains(Path.GetFileName(directory))) pending.Push(directory);
                }

                foreach (var path in Directory.EnumerateFiles(current, "*.cs", Listing))
                {
                    var info = new FileInfo(path);
                    files.Add(new SourceFile(ToRelative(root, path), info.Length, info.LastWriteTimeUtc.Ticks));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A folder that vanished or cannot be listed has no source to show;
                // the rest of the component still does.
            }
        }

        // Case-insensitive so Order.cs sorts before Order.Totals.cs, the order a
        // file tree shows them in, and a partial type's first part is its main one.
        files.Sort((left, right) =>
        {
            var order = StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath);
            return order != 0 ? order : StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath);
        });
        return files;
    }

    private static bool IsWithin(string root, string path)
    {
        if (string.Equals(root, path, PathComparison)) return true;

        // A root at a drive's top (C:\) already ends in its separator.
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string ToRelative(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative == "." ? string.Empty : relative;
    }

    private readonly record struct SourceFile(string RelativePath, long Length, long WriteTicks);

    private sealed record Entry(IReadOnlyList<SourceFile> Files, CodeModel Model);
}
