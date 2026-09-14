using System.Text.RegularExpressions;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// Writes the <c>status</c> field of the <c>meta</c> fence belonging to one
/// knowledge heading — a chapter's, addressed by <c>&lt;path&gt;#&lt;slug&gt;</c>, or a
/// file's own, addressed by the bare path.
///
/// <para>Two verbs, and deliberately not one verb taking a nullable word.
/// Removing a status is a different operation from setting one, not a
/// degenerate case of it: <see cref="UpdateStatus"/> refuses a blank because a
/// blank is not a member of any folder's vocabulary, and a caller that means to
/// clear has to say <see cref="RemoveStatus"/>. Had clearing been "set it to
/// null" instead, every guard already written as
/// <c>IsNullOrWhiteSpace</c> would have kept compiling and silently turned a
/// clear into a no-op, and an accidental null anywhere upstream would have
/// become a destructive write in folders where the status is required.</para>
/// </summary>
internal static class KnowledgeMarkdownStatusWriter
{
    private static readonly Regex Heading = new("^(#{1,6})[ \\t]+(.+?)\\s*$", RegexOptions.Compiled);

    /// <summary>Set the heading's status, inserting the field — or the whole
    /// fence — when it is not there yet.</summary>
    public static void UpdateStatus(string folderRoot, string itemPath, string folderPrefix, string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        var document = Open(folderRoot, itemPath, folderPrefix);
        UpsertStatus(document.Lines, document.HeadingIndex, status.Trim().ToLowerInvariant());
        document.Save();
    }

    /// <summary>
    /// Delete the heading's <c>status</c> line, leaving the fence and every other
    /// line exactly as they were.
    ///
    /// <para><b>The fence is never removed, even when the status was the only
    /// thing in it.</b> The fence is what marks a heading as an addressable
    /// chapter — the index generator makes one node per heading that carries a
    /// <c>meta</c> block — and in <c>.arc42</c> and <c>.design</c>, which define no
    /// <c>type</c> field, clearing the status routinely empties the block. An
    /// empty fence is still a chapter; a heading with no fence is not one, so
    /// tidying the fence away would silently drop the chapter out of the graph.</para>
    ///
    /// <para>A no-op where there is nothing to remove — a heading with no fence,
    /// or a fence that states no status. Neither is an error: both are already the
    /// state the caller asked for, and the file is left untouched rather than
    /// rewritten identically.</para>
    /// </summary>
    public static void RemoveStatus(string folderRoot, string itemPath, string folderPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPrefix);

        var document = Open(folderRoot, itemPath, folderPrefix);
        if (!TryRemoveStatus(document.Lines, document.HeadingIndex)) return;

        document.Save();
    }

    /// <summary>Resolve the item path to a file inside the folder, read it, and
    /// find the addressed heading. The half both verbs share.</summary>
    private static HeadingDocument Open(string folderRoot, string itemPath, string folderPrefix)
    {
        var (relativePath, anchor) = SplitItemPath(itemPath);
        if (!relativePath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Knowledge item path must be inside {folderPrefix.TrimEnd('/')}: {itemPath}");
        }

        var filePath = Path.GetFullPath(Path.Combine(folderRoot, relativePath[folderPrefix.Length..].Replace('/', Path.DirectorySeparatorChar)));
        var normalizedRoot = Path.GetFullPath(folderRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!filePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Knowledge item path escapes the knowledge root: {itemPath}");
        }

        if (!File.Exists(filePath)) throw new FileNotFoundException($"Knowledge item file was not found: {relativePath}", filePath);

        var text = File.ReadAllText(filePath);

        // The file's own newline, kept and written back. Removing a line changes
        // the line count, which is exactly where rejoining with the wrong newline
        // would turn a one-field edit into a whole-file diff.
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

        var headingIndex = FindHeading(lines, anchor);
        if (headingIndex < 0) throw new InvalidOperationException($"Knowledge item heading was not found: {itemPath}");

        return new HeadingDocument(filePath, newline, lines, headingIndex);
    }

    /// <summary>One knowledge file, opened at the heading a write is addressed
    /// to.</summary>
    private sealed record HeadingDocument(string FilePath, string Newline, List<string> Lines, int HeadingIndex)
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
    private static int FindFence(IReadOnlyList<string> lines, int headingIndex)
    {
        var index = headingIndex + 1;
        while (index < lines.Count && string.IsNullOrWhiteSpace(lines[index])) index++;

        return index < lines.Count && string.Equals(lines[index].Trim(), "```meta", StringComparison.OrdinalIgnoreCase)
            ? index
            : -1;
    }

    /// <summary>The index of the <c>status</c> line inside the fence opened at
    /// <paramref name="fenceIndex"/>, or -1 when the fence states none. The scan
    /// stops at the closing fence so a <c>status:</c> further down the document is
    /// never mistaken for this chapter's.</summary>
    private static int FindStatusLine(IReadOnlyList<string> lines, int fenceIndex)
    {
        for (var i = fenceIndex + 1; i < lines.Count && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal); i++)
        {
            if (lines[i].TrimStart().StartsWith("status:", StringComparison.OrdinalIgnoreCase)) return i;
        }

        return -1;
    }

    private static void UpsertStatus(List<string> lines, int headingIndex, string status)
    {
        var fenceIndex = FindFence(lines, headingIndex);
        if (fenceIndex >= 0)
        {
            var statusLine = FindStatusLine(lines, fenceIndex);
            if (statusLine >= 0)
            {
                var indent = lines[statusLine][..(lines[statusLine].Length - lines[statusLine].TrimStart().Length)];
                lines[statusLine] = $"{indent}status: {status}";
            }
            else
            {
                lines.Insert(fenceIndex + 1, $"status: {status}");
            }

            return;
        }

        lines.InsertRange(headingIndex + 1, [string.Empty, "```meta", $"status: {status}", "```"]);
    }

    /// <summary>Whether a status line was found and removed. False leaves the
    /// document untouched, so the caller can skip the write entirely.</summary>
    private static bool TryRemoveStatus(List<string> lines, int headingIndex)
    {
        var fenceIndex = FindFence(lines, headingIndex);
        if (fenceIndex < 0) return false;

        var statusLine = FindStatusLine(lines, fenceIndex);
        if (statusLine < 0) return false;

        lines.RemoveAt(statusLine);
        return true;
    }

    private static string Slug(string heading)
    {
        var chars = heading
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
