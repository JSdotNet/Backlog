using Backlog.Modules.Devbook.Abstractions;
using Backlog.UI.Components.Devbook;
using System.Text.RegularExpressions;

namespace Backlog.Desktop.UI.Devbook;

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
///
/// <para>The folder is read off the prefix the caller already hands in, in
/// either spelling — <c>.arc42/</c> or <c>.devbook/arc42/</c> — because three
/// contract-16 rules depend on it, and every one of them is about what ends up in
/// the file. Where a folder rests at <c>active</c> by omission, setting
/// <c>active</c> deletes the line rather than writing the spelling the convention
/// reports. A decision rung outside <c>domain/</c> is refused, as a blank is: it
/// is not in that folder's vocabulary. And in <c>domain/</c>, a write that moves a
/// chapter off a rung takes the record that no longer stands with it, in the same
/// write — see <see cref="RecordFieldsThatNoLongerStand"/>.</para>
/// </summary>
internal static class DevbookMarkdownStatusWriter
{
    private static readonly Regex Heading = new("^(#{1,6})[ \\t]+(.+?)\\s*$", RegexOptions.Compiled);

    /// <summary>Set the heading's status, inserting the field — or the whole
    /// fence — when it is not there yet. The resting value in a folder that rests
    /// by omission is a removal instead, and a decision rung outside
    /// <c>domain/</c> is refused.</summary>
    public static void UpdateStatus(string folderRoot, string itemPath, string folderPrefix, string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        var folder = DevbookFolders.FromPath(folderPrefix);
        var value = status.Trim().ToLowerInvariant();

        // Refused before the file is opened, so a refused write leaves no trace.
        // Outside domain/ a rung is not in the folder's vocabulary at all, and
        // writing one would put a violation into the file on purpose.
        if (DevbookSchema.IsDecisionRung(value) && !DevbookSchema.AllowsDecisionRungs(folder))
        {
            throw new ArgumentException(
                $"'{value}' is a decision rung, and only domain/ has decision rungs: {itemPath}", nameof(status));
        }

        if (DevbookSchema.IsResting(folder, value))
        {
            RemoveStatus(folderRoot, itemPath, folderPrefix);
            return;
        }

        var document = Open(folderRoot, itemPath, folderPrefix);
        UpsertStatus(document.Lines, document.HeadingIndex, value);

        if (DevbookSchema.AllowsDecisionRungs(folder))
        {
            var fenceIndex = FindFence(document.Lines, document.HeadingIndex);
            RemoveFields(document.Lines, fenceIndex, RecordFieldsThatNoLongerStand(value));

            // Writing a rung is what ends a review: the rule has approval delete
            // the triad in the same change, because an approved chapter carries
            // the decision and not the road to it. The product offers no rung, so
            // this is only reached by an approval gate calling in directly.
            if (DevbookSchema.IsDecisionRung(value)) RemoveFields(document.Lines, fenceIndex, DevbookSchema.ReviewFields);
        }

        document.Save();
    }

    /// <summary>
    /// Delete the heading's <c>status</c> line, leaving the fence and every other
    /// line exactly as they were — save, in <c>domain/</c>, a decision record,
    /// which cannot stand once the rung it records is gone.
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
    /// or a fence that states no status and carries no stranded record. Neither is
    /// an error: both are already the state the caller asked for, and the file is
    /// left untouched rather than rewritten identically.</para>
    /// </summary>
    public static void RemoveStatus(string folderRoot, string itemPath, string folderPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPrefix);

        var folder = DevbookFolders.FromPath(folderPrefix);
        var document = Open(folderRoot, itemPath, folderPrefix);

        var changed = TryRemoveStatus(document.Lines, document.HeadingIndex);
        if (DevbookSchema.AllowsDecisionRungs(folder))
        {
            var fenceIndex = FindFence(document.Lines, document.HeadingIndex);
            changed |= RemoveFields(document.Lines, fenceIndex, RecordFieldsThatNoLongerStand(null)) > 0;
        }

        if (!changed) return;

        document.Save();
    }

    /// <summary>
    /// The decision-record fields that cannot stand beside
    /// <paramref name="status"/>: none under <c>accepted</c>, which stands on both
    /// records; the three <c>accepted-*</c> under <c>approved</c>, since the
    /// acceptance is gone and the approval it stood on is not; and all six under
    /// anything else, absent included, because the chapter is off the rungs
    /// altogether.
    ///
    /// <para>Deleted in the same write as the status, as the rule asks. A record
    /// left behind on a chapter no longer claiming its rung is itself reported, so
    /// a status change that stranded one would trade one violation for another.
    /// Asked by <see cref="DevbookChapterText"/> too, whose merge applies a status
    /// by the same rule.</para>
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
    /// closing fence, as <see cref="FindStatusLine"/>'s does, so a field of the
    /// same name under another heading is never touched. Returns how many lines
    /// went.</summary>
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
    /// find the addressed heading. The half both verbs share.</summary>
    private static HeadingDocument Open(string folderRoot, string itemPath, string folderPrefix)
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
