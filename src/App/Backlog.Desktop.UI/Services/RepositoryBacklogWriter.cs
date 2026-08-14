using System.Text.RegularExpressions;
using Backlog.Modules.Backlog.DomainModels;

namespace Backlog.Desktop.UI.Services;

/// <summary>
/// Writes edits back to repository-authored <c>.backlog</c> Markdown files.
/// Each file may contain multiple top-level segments (split on <c>#</c> headings).
/// Writes target a specific segment by index, preserving <c>meta</c> blocks,
/// other segments, and the file's original newline style.
/// </summary>
internal static class RepositoryBacklogWriter
{
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})[ \t]+(.*)$", RegexOptions.Compiled);

    /// <summary>Updates only the <c>status:</c> field inside the <c>```meta</c>
    /// block of the segment at <paramref name="segmentIndex"/>. Other fields
    /// in the meta block and other segments are left untouched.</summary>
    public static void UpdateSegmentStatus(string filePath, int segmentIndex, string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Backlog file not found: {filePath}", filePath);

        var text = File.ReadAllText(filePath);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

        var (start, end) = FindSegmentBounds(lines, segmentIndex);
        if (start < 0)
            throw new InvalidOperationException($"Segment {segmentIndex} not found in {filePath}");

        UpsertStatusInRange(lines, start, end, status);
        File.WriteAllText(filePath, string.Join(newline, lines));
    }

    /// <summary>
    /// Replaces the entire content of a segment at <paramref name="segmentIndex"/>
    /// with <paramref name="newRawText"/>. The raw text uses backlog sigils
    /// (<c>`!status`</c>), so this method translates the sigil status back to the
    /// knowledge <c>meta</c> block vocabulary and preserves non-status meta fields.
    /// </summary>
    public static void UpdateSegment(string filePath, int segmentIndex, string newRawText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Backlog file not found: {filePath}", filePath);

        var text = File.ReadAllText(filePath);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

        var (start, end) = FindSegmentBounds(lines, segmentIndex);
        if (start < 0)
            throw new InvalidOperationException($"Segment {segmentIndex} not found in {filePath}");

        // Extract the existing meta block fields (if any) from the original segment.
        var existingMeta = ExtractMetaFields(lines, start, end);

        // Parse the new raw text to extract the status from backlog sigils.
        var parsed = EntryTextParser.Parse(newRawText);
        if (parsed.Status is { } entryStatus)
        {
            existingMeta["status"] = RepositoryBacklogText.ToKnowledgeStatus(entryStatus);
        }

        // Build the replacement lines: title + body from new raw text, meta block from merged fields.
        var newNormalized = newRawText.Replace("\r\n", "\n").Replace('\r', '\n');
        var newLines = BuildSegmentWithMeta(newNormalized, existingMeta);

        // Replace the segment in the file.
        lines.RemoveRange(start, end - start);
        lines.InsertRange(start, newLines);

        File.WriteAllText(filePath, string.Join(newline, lines));
    }

    /// <summary>
    /// Writes edited backlog row text back to the repository <c>.backlog</c> file.
    /// Translates sigil metadata to knowledge meta block vocabulary.
    /// Uses <see cref="AppSaveState"/> patterns for error visibility.
    /// </summary>
    public static void SaveRowToSource(RepositoryBacklogOrigin origin, string rawText)
    {
        ArgumentNullException.ThrowIfNull(origin);

        // Parse the edited text to extract the status sigil.
        var parsed = EntryTextParser.Parse(rawText);

        // Read the original file to get existing meta fields.
        if (!File.Exists(origin.FilePath))
            throw new FileNotFoundException($"Backlog source file not found: {origin.FilePath}", origin.FilePath);

        var text = File.ReadAllText(origin.FilePath);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

        var (start, end) = FindSegmentBounds(lines, origin.SegmentIndex);
        if (start < 0)
            throw new InvalidOperationException($"Segment {origin.SegmentIndex} not found in {origin.FilePath}");

        // Extract existing meta fields from the original segment.
        var existingMeta = ExtractMetaFields(lines, start, end);

        // Translate sigil status to knowledge vocabulary.
        if (parsed.Status is { } entryStatus)
        {
            existingMeta["status"] = RepositoryBacklogText.ToKnowledgeStatus(entryStatus);
        }

        // Build new segment lines with the meta block.
        var newNormalized = rawText.Replace("\r\n", "\n").Replace('\r', '\n');
        var newLines = BuildSegmentWithMeta(newNormalized, existingMeta);

        // Replace the segment in the file.
        lines.RemoveRange(start, end - start);
        lines.InsertRange(start, newLines);

        File.WriteAllText(origin.FilePath, string.Join(newline, lines));
    }

    /// <summary>Finds the line range [start, end) of a segment by its index
    /// (0-based). Segments are split on top-level <c>#</c> headings.</summary>
    private static (int Start, int End) FindSegmentBounds(IReadOnlyList<string> lines, int segmentIndex)
    {
        var boundaries = new List<int>();
        var seenFirstContent = false;
        var inFence = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                seenFirstContent = true;
                continue;
            }

            if (inFence || trimmed.Length == 0) continue;

            var isTopHeading = trimmed.StartsWith("# ", StringComparison.Ordinal);

            if (!seenFirstContent)
            {
                seenFirstContent = true;
                if (isTopHeading) boundaries.Add(i);
                continue;
            }

            if (isTopHeading) boundaries.Add(i);
        }

        if (boundaries.Count == 0)
        {
            return segmentIndex == 0 ? (0, lines.Count) : (-1, -1);
        }

        if (segmentIndex >= boundaries.Count)
            return (-1, -1);

        var start = boundaries[segmentIndex];
        var end = segmentIndex + 1 < boundaries.Count ? boundaries[segmentIndex + 1] : lines.Count;
        return (start, end);
    }

    /// <summary>Extracts all fields from the first <c>```meta</c> block found
    /// within the given line range.</summary>
    private static Dictionary<string, string> ExtractMetaFields(IReadOnlyList<string> lines, int start, int end)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inMeta = false;

        for (var i = start; i < end; i++)
        {
            var trimmed = lines[i].Trim();

            if (inMeta)
            {
                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    break; // End of first meta block.
                }

                var separator = trimmed.IndexOf(':');
                if (separator > 0)
                {
                    var key = trimmed[..separator].Trim();
                    var value = trimmed[(separator + 1)..].Trim();
                    if (key.Length > 0 && value.Length > 0)
                    {
                        fields[key] = value;
                    }
                }
                continue;
            }

            if (trimmed.Equals("```meta", StringComparison.OrdinalIgnoreCase))
            {
                inMeta = true;
            }
        }

        return fields;
    }

    /// <summary>Builds segment lines from the backlog raw text, stripping the
    /// sigil meta line and inserting a knowledge-style <c>```meta</c> block
    /// with the merged fields.</summary>
    private static List<string> BuildSegmentWithMeta(string rawText, Dictionary<string, string> metaFields)
    {
        var lines = rawText.TrimEnd('\n').Split('\n').ToList();
        var result = new List<string>();

        // Find and add the title line.
        var titleIndex = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            titleIndex = i;
            break;
        }

        if (titleIndex < 0)
        {
            return [.. lines];
        }

        // Add lines up to and including the title.
        for (var i = 0; i <= titleIndex; i++)
        {
            result.Add(lines[i]);
        }

        // Skip the sigil meta line if present.
        var metaLineIndex = titleIndex + 1;
        while (metaLineIndex < lines.Count && string.IsNullOrWhiteSpace(lines[metaLineIndex])) metaLineIndex++;

        var hasSigilMetaLine = metaLineIndex < lines.Count && EntryTextParser.IsMetadataLine(lines[metaLineIndex]);
        var bodyStart = hasSigilMetaLine ? metaLineIndex + 1 : titleIndex + 1;

        // Insert the knowledge meta block if there are fields.
        if (metaFields.Count > 0)
        {
            result.Add(string.Empty);
            result.Add("```meta");
            foreach (var (key, value) in metaFields)
            {
                result.Add($"{key}: {value}");
            }
            result.Add("```");
        }

        // Add the remaining body lines.
        for (var i = bodyStart; i < lines.Count; i++)
        {
            result.Add(lines[i]);
        }

        // Ensure trailing newline for segment separation.
        if (result.Count > 0 && !string.IsNullOrWhiteSpace(result[^1]))
        {
            result.Add(string.Empty);
        }

        return result;
    }

    /// <summary>Upserts the <c>status:</c> field inside the first meta block
    /// found within the segment range, or inserts a new meta block after the
    /// heading.</summary>
    private static void UpsertStatusInRange(List<string> lines, int start, int end, string status)
    {
        // Find the first meta block in the segment.
        for (var i = start; i < end && i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (!trimmed.Equals("```meta", StringComparison.OrdinalIgnoreCase)) continue;

            // Found meta block — look for status line.
            var close = i + 1;
            var statusLine = -1;
            while (close < lines.Count && !lines[close].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (lines[close].TrimStart().StartsWith("status:", StringComparison.OrdinalIgnoreCase))
                    statusLine = close;
                close++;
            }

            if (statusLine >= 0)
            {
                var indent = lines[statusLine][..(lines[statusLine].Length - lines[statusLine].TrimStart().Length)];
                lines[statusLine] = $"{indent}status: {status}";
            }
            else
            {
                lines.Insert(i + 1, $"status: {status}");
            }
            return;
        }

        // No meta block found — insert one after the heading.
        var headingIndex = start;
        for (var i = start; i < end && i < lines.Count; i++)
        {
            if (HeadingRegex.IsMatch(lines[i].TrimStart()))
            {
                headingIndex = i;
                break;
            }
        }

        lines.InsertRange(headingIndex + 1, [string.Empty, "```meta", $"status: {status}", "```"]);
    }
}
