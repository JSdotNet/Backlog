using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel.Devbook;
using System.Text.RegularExpressions;

namespace Backlog.Infrastructure.Devbook.Chapters;

/// <summary>
/// How a write into a knowledge file finds what it writes: the file an item path
/// names inside a folder, the heading it is addressed to, that heading's
/// <c>meta</c> fence, and the fields in it.
///
/// <para>Every writer that addresses a heading is built on them —
/// <see cref="DevbookMarkdownRatingWriter"/> for a rating in <c>tech/</c> and
/// <c>ai/</c>, <see cref="DevbookMarkdownSyncWriter"/> for a <c>sync</c>
/// direction — and <see cref="DevbookChapterStatusText"/> asks them for the
/// decision-record rule its merge applies. One set of helpers means every writer
/// finds a heading the same way and names it with the same slug. None of them
/// writes a chapter's workflow state: under devbook's branch-true decision that
/// state is the tool's, not the file's.</para>
/// </summary>
internal static class DevbookHeadingDocument
{
    internal static readonly Regex Heading = new("^(#{1,6})[ \\t]+(.+?)\\s*$", RegexOptions.Compiled);

    /// <summary>
    /// The decision-record fields that cannot stand beside
    /// <paramref name="status"/>: none under <c>accepted</c>, which stands on both
    /// records; the three <c>accepted-*</c> under <c>approved</c>, since the
    /// acceptance is gone and the approval it stood on is not; and all six under
    /// anything else, absent included, because the chapter is off the rungs
    /// altogether.
    ///
    /// <para>Asked by <see cref="DevbookChapterStatusText"/>, whose merge applies
    /// a status the disk gained under an open editor by this rule, so the buffer
    /// written back never restores a record the change had deleted.</para>
    /// </summary>
    internal static IReadOnlyList<string> RecordFieldsThatNoLongerStand(string? status)
    {
        var value = status?.Trim();
        if (string.Equals(value, DevbookSchema.DecisionRungs[1], StringComparison.OrdinalIgnoreCase)) return [];
        if (string.Equals(value, DevbookSchema.DecisionRungs[0], StringComparison.OrdinalIgnoreCase)) return DevbookSchema.AcceptanceRecordFields;

        return DevbookSchema.DecisionRecordFields;
    }

    /// <summary>Delete the named fields from the fence opened at
    /// <paramref name="fenceIndex"/>, and only from it: the scan stops at the
    /// closing fence, so a field of the same name under another heading is never
    /// touched. Returns how many lines went.</summary>
    internal static int RemoveFields(List<string> lines, int fenceIndex, IReadOnlyList<string> fields)
    {
        if (fenceIndex < 0 || fields.Count == 0) return 0;

        var removed = 0;
        var i = fenceIndex + 1;
        while (i < lines.Count && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            if (IsField(lines[i], fields))
            {
                lines.RemoveAt(i);
                removed++;
                continue;
            }

            i++;
        }

        return removed;
    }

    private static bool IsField(string line, IReadOnlyList<string> fields)
    {
        var trimmed = line.TrimStart();
        var colon = trimmed.IndexOf(':');
        if (colon <= 0) return false;

        return fields.Contains(trimmed[..colon].Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Resolve the item path to a file inside the folder, read it, and
    /// find the addressed heading — a chapter's, by <c>&lt;path&gt;#&lt;slug&gt;</c>,
    /// or the file's own, by the bare path.</summary>
    internal static HeadingDocument Open(string folderRoot, string itemPath, string folderPrefix)
    {
        var (relativePath, anchor) = SplitItemPath(itemPath);

        // A reader spells its paths relative to the repository, so a folder in the
        // devbook layout names its chapters .devbook/arc42/... where one at the
        // legacy root names them .arc42/... — the same folder either way.
        var prefix = new[] { DevbookLayoutPrefix(folderPrefix), folderPrefix }
            .FirstOrDefault(candidate => relativePath.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Devbook item path must be inside {folderPrefix.TrimEnd('/')}: {itemPath}");

        var filePath = Path.GetFullPath(Path.Combine(folderRoot, relativePath[prefix.Length..].Replace('/', Path.DirectorySeparatorChar)));
        var normalizedRoot = Path.GetFullPath(folderRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!filePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Devbook item path escapes the devbook root: {itemPath}");
        }

        if (!File.Exists(filePath)) throw new FileNotFoundException($"Devbook item file was not found: {relativePath}", filePath);

        var text = File.ReadAllText(filePath);

        // The file's own newline, kept and written back. Removing a line changes
        // the line count, which is exactly where rejoining with the wrong newline
        // would turn a one-field edit into a whole-file diff.
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

        var headingIndex = FindHeading(lines, anchor);
        if (headingIndex < 0) throw new InvalidOperationException($"Devbook item heading was not found: {itemPath}");

        return new HeadingDocument(filePath, newline, lines, headingIndex);
    }

    /// <summary>The same folder's prefix in the devbook layout:
    /// <c>.arc42/</c> becomes <c>.devbook/arc42/</c>.</summary>
    private static string DevbookLayoutPrefix(string folderPrefix) =>
        $"{DevbookFolderSetting.DevbookRoot}/{folderPrefix.TrimStart('.')}";

    /// <summary>One knowledge file, opened at the heading a write is addressed
    /// to.</summary>
    internal sealed record HeadingDocument(string FilePath, string Newline, List<string> Lines, int HeadingIndex)
    {
        public void Save() => File.WriteAllText(FilePath, string.Join(Newline, Lines));
    }

    private static (string RelativePath, string? Anchor) SplitItemPath(string itemPath)
    {
        var parts = itemPath.Split('#', 2, StringSplitOptions.TrimEntries);
        return (parts[0], parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : null);
    }

    private static int FindHeading(IReadOnlyList<string> lines, string? anchor)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var match = Heading.Match(lines[i]);
            if (!match.Success) continue;
            if (anchor is null && match.Groups[1].Value.Length == 1) return i;
            if (anchor is not null && string.Equals(Slug(match.Groups[2].Value.Trim()), anchor, StringComparison.OrdinalIgnoreCase)) return i;
        }

        return -1;
    }

    /// <summary>The index of the heading's <c>meta</c> fence opener, or -1 when the
    /// heading carries no fence. Blank lines between the heading and the fence are
    /// skipped, because that is how the convention writes them.</summary>
    internal static int FindFence(IReadOnlyList<string> lines, int headingIndex)
    {
        var index = headingIndex + 1;
        while (index < lines.Count && string.IsNullOrWhiteSpace(lines[index])) index++;

        return index < lines.Count && string.Equals(lines[index].Trim(), "```meta", StringComparison.OrdinalIgnoreCase)
            ? index
            : -1;
    }

    internal static string Slug(string heading)
    {
        var chars = heading
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
