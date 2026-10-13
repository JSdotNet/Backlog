using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// Which knowledge folder a chapter path names, and where the file sits inside
/// it.
/// <para>
/// A session spells a chapter the way the rest of the product does — repository
/// relative, with the area folder on the front (<c>.devbook/arc42/adr/0012-….md</c>) —
/// because that is the spelling a <c>related:</c> field carries and the one a
/// person copies out of a panel. This takes it apart: the leading segment names
/// the folder, the remainder is the path inside it.
/// </para>
/// <para>
/// The prefixes a folder answers to are read from the folder settings rather than
/// written out again, so a repository that points <c>.arc42</c> at
/// <c>docs/arch</c> is matched on both spellings and a folder whose default moves
/// does not leave a second copy of it here. The undotted spelling is accepted
/// because the panels present an area with its leading dot trimmed, so that is
/// what a reader copies. The rules mirror <c>DevbookChapterResolver</c>, which is
/// the same decision made for the editing surface and which lives in a module's
/// UI project this one may not reference.
/// </para>
/// </summary>
internal static class ChapterPaths
{
    /// <summary>One spelling for a chapter: forward slashes, no anchor, no
    /// leading <c>./</c> or <c>/</c>. The anchor is dropped because a chapter is a
    /// file — a heading inside it is the same file.</summary>
    internal static string Normalize(string? path)
    {
        var forward = (path ?? string.Empty).Replace('\\', '/').Trim();

        var anchor = forward.IndexOf('#', StringComparison.Ordinal);
        if (anchor >= 0) forward = forward[..anchor].Trim();

        while (forward.StartsWith("./", StringComparison.Ordinal)) forward = forward[2..];

        return forward.TrimStart('/');
    }

    /// <summary>
    /// The folder a normalized path belongs to and the path within it, or null
    /// when no configured folder claims it.
    /// <para>
    /// A folder whose conventional path is empty — Instructions, whose root is the
    /// repository itself — claims whatever nothing else does, which is what lets
    /// <c>.github/copilot-instructions.md</c> resolve with its leading dot intact.
    /// It is tried last so a path under a real area folder is never swallowed by
    /// it.
    /// </para>
    /// </summary>
    internal static (DevbookFolderSetting Folder, string RelativePath)? Split(
        IReadOnlyList<DevbookFolderSetting> folders,
        string normalizedPath)
    {
        if (normalizedPath.Length == 0) return null;

        foreach (var folder in folders)
        {
            foreach (var prefix in Prefixes(folder))
            {
                if (!StartsWithSegment(normalizedPath, prefix)) continue;

                return (folder, normalizedPath[(prefix.Length + 1)..]);
            }
        }

        var root = folders.FirstOrDefault(folder => string.IsNullOrWhiteSpace(folder.DefaultRelativePath));

        return root is null ? null : (root, normalizedPath);
    }

    /// <summary>
    /// Whether a path a folder claims is something this server will open.
    /// <para>
    /// <b>The containment check is not enough on its own, and this is why.</b>
    /// <see cref="ResolveWithin"/> refuses a path that climbs out of the folder's
    /// root — but the Instructions folder's root <em>is</em> the clone, so every
    /// file in the repository stays inside it. Without this,
    /// <c>chapterPath: ".git/config"</c> is contained, resolved, read, and
    /// answered as a chapter.
    /// </para>
    /// <para>
    /// So the root is closed from the other end. Under a real area folder — one
    /// with a conventional path of its own — a chapter is a Markdown file and
    /// nothing else is. Under the Instructions folder, whose area is the whole
    /// clone, it is what the product already recognises as an instruction
    /// document: <see cref="DevbookInstructionSources.IsInstructionSource"/>, the
    /// same predicate the Instructions panel discovers by. A path no folder
    /// claims falls to Instructions in <see cref="Split"/> and meets that
    /// predicate here, which is what refuses an area prefix nobody configured.
    /// </para>
    /// </summary>
    internal static bool IsReadableChapter(DevbookFolderSetting folder, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(folder.DefaultRelativePath))
        {
            return DevbookInstructionSources.IsInstructionSource(relativePath);
        }

        var path = Normalize(relativePath);

        if (path.Length == 0) return false;
        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return false;

        var segments = path.Split('/');

        return !segments.Any(segment => segment is "." or ".." || segment.Length == 0)
            && !segments.Any(DevbookInstructionSources.IsExcludedName);
    }

    /// <summary>
    /// Whether a path relative to the Brain's root is a note this server will
    /// open.
    /// <para>
    /// The Brain's own rule rather than <see cref="IsReadableChapter"/>, which
    /// would read the Brain's empty conventional path as the Instructions folder
    /// and admit only <c>CLAUDE.md</c> and its kind. A Brain note is any Markdown
    /// file in the folder — and nothing under a dot-folder, which is where a
    /// notes app keeps its own state (<c>.obsidian</c>, <c>.trash</c>) and which
    /// the Brain's own listing never walks either. The excluded build and tool
    /// folders are refused for the reason they are everywhere else.
    /// </para>
    /// <para>
    /// A syntactic check: containment is still <see cref="ResolveWithin"/>'s, on
    /// the resolved path, because a rooted path passes every rule here and is
    /// thrown away by <c>Path.Combine</c>'s answer, not by its input.
    /// </para>
    /// </summary>
    internal static bool IsReadableBrainNote(string relativePath)
    {
        var path = Normalize(relativePath);

        if (path.Length == 0) return false;
        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return false;

        var segments = path.Split('/');

        // A colon is never part of a note's name: on NTFS it names an alternate
        // data stream of a file (plan.md:x.md), and on a drive-letter path it is
        // a root somewhere else entirely.
        return !segments.Any(segment => segment.Length == 0 || segment.StartsWith('.') || segment.Contains(':'))
            && !segments.Any(DevbookInstructionSources.IsExcludedName);
    }

    /// <summary>
    /// Whether the file at <paramref name="fullPath"/>, or any folder between it
    /// and <paramref name="rootPath"/>, is a symbolic link or a junction.
    /// <para>
    /// The root itself is not asked: it is the folder the person picked, and a
    /// Brain kept behind a link of their own making is their arrangement, not an
    /// escape. Everything below it is reached by a path a client sent, and
    /// <see cref="ResolveWithin"/> only checks how that path is spelled. A path
    /// that cannot be inspected counts as a link — refusing a note is cheaper
    /// than reading something that is not one.
    /// </para>
    /// </summary>
    internal static bool PassesThroughLink(string rootPath, string fullPath)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));

            if (new FileInfo(fullPath).LinkTarget is not null) return true;

            for (var directory = Path.GetDirectoryName(fullPath);
                 directory is not null && directory.Length > root.Length;
                 directory = Path.GetDirectoryName(directory))
            {
                if (new DirectoryInfo(directory).LinkTarget is not null) return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return true;
        }
    }

    /// <summary>The folder names a path may carry: the key, the configured path,
    /// its last segment, and each of those without a leading dot.</summary>
    private static IEnumerable<string> Prefixes(DevbookFolderSetting folder)
    {
        var seen = new List<string>();

        foreach (var candidate in new[] { folder.EffectivePath, folder.Key })
        {
            var normalized = Normalize(candidate).TrimEnd('/');
            if (normalized.Length == 0) continue;

            // A rooted override names somewhere off the clone entirely, so the
            // whole of it is not a prefix any selection carries; its last segment
            // still is, because that is the folder the reader walked.
            if (!Path.IsPathRooted(normalized)) Add(normalized);

            var lastSeparator = normalized.LastIndexOf('/');
            if (lastSeparator >= 0) Add(normalized[(lastSeparator + 1)..]);
        }

        return seen;

        void Add(string prefix)
        {
            AddOne(prefix);
            if (prefix.StartsWith('.')) AddOne(prefix[1..]);
        }

        void AddOne(string prefix)
        {
            if (prefix.Length > 0 && !seen.Contains(prefix, StringComparer.OrdinalIgnoreCase)) seen.Add(prefix);
        }
    }

    private static bool StartsWithSegment(string path, string segment) =>
        path.Length > segment.Length
        && path[segment.Length] == '/'
        && path.StartsWith(segment, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The full path of a relative path under a root, or null when it does not
    /// stay there.
    /// <para>
    /// The containment check is the reason this exists rather than a
    /// <c>Path.Combine</c>: the relative path arrives from a client over the wire,
    /// and <c>Path.Combine</c> hands back the absolute path and discards the root
    /// entirely when the second argument is rooted. So the check is on the result
    /// rather than on the input, exactly as <c>DevbookChapterPaths.ResolveWithin</c>
    /// does it for the editing surface.
    /// </para>
    /// </summary>
    internal static string? ResolveWithin(string rootPath, string relativePath)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

            return fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
