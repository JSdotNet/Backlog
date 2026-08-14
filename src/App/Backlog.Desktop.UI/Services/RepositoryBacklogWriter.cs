using System.Text.RegularExpressions;
using Backlog.Modules.Backlog.DomainModels;

namespace Backlog.Desktop.UI.Services;

/// <summary>
/// Writes edits back to repository-authored <c>.backlog</c> Markdown files.
/// Each file may contain multiple top-level segments (split on <c>#</c> headings).
/// Writes target a specific segment by index, preserving other segments and the
/// file's original newline style.
/// </summary>
internal static class RepositoryBacklogWriter
{
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})[ \t]+(.*)$", RegexOptions.Compiled);

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
        WriteAllText(filePath, newline, lines);
    }

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

        var status = ReadStatusFromRawText(newRawText) ?? ReadStatusFromSegment(lines, start, end);
        var newNormalized = newRawText.Replace("\r\n", "\n").Replace('\r', '\n');
        var newLines = BuildSegmentWithMeta(newNormalized, status);

        lines.RemoveRange(start, end - start);
        lines.InsertRange(start, newLines);

        WriteAllText(filePath, newline, lines);
    }

    public static void SaveRowToSource(RepositoryBacklogOrigin origin, string rawText)
    {
        ArgumentNullException.ThrowIfNull(origin);

        if (!File.Exists(origin.FilePath))
            throw new FileNotFoundException($"Backlog source file not found: {origin.FilePath}", origin.FilePath);

        var text = File.ReadAllText(origin.FilePath);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

        var (start, end) = FindSegmentBounds(lines, origin.SegmentIndex);
        if (start < 0)
            throw new InvalidOperationException($"Segment {origin.SegmentIndex} not found in {origin.FilePath}");

        var status = ReadStatusFromRawText(rawText) ?? ReadStatusFromSegment(lines, start, end);
        var newNormalized = rawText.Replace("\r\n", "\n").Replace('\r', '\n');
        var newLines = BuildSegmentWithMeta(newNormalized, status);

        lines.RemoveRange(start, end - start);
        lines.InsertRange(start, newLines);

        WriteAllText(origin.FilePath, newline, lines);
    }

    public static void DeleteSegment(string filePath, int segmentIndex)
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

        lines.RemoveRange(start, end - start);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count == 0)
        {
            File.WriteAllText(filePath, string.Empty);
            return;
        }

        WriteAllText(filePath, newline, lines);
    }

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

    private static string? ReadStatusFromSegment(IReadOnlyList<string> lines, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            var trimmed = lines[i].Trim();
            if (!trimmed.Equals("```meta", StringComparison.OrdinalIgnoreCase)) continue;

            for (var j = i + 1; j < end && j < lines.Count; j++)
            {
                var candidate = lines[j].Trim();
                if (candidate.StartsWith("```", StringComparison.Ordinal)) break;

                var separator = candidate.IndexOf(':');
                if (separator <= 0) continue;

                var key = candidate[..separator].Trim();
                if (!key.Equals("status", StringComparison.OrdinalIgnoreCase)) continue;

                var value = candidate[(separator + 1)..].Trim();
                if (value.Length > 0) return value;
            }
        }

        return null;
    }

    private static string? ReadStatusFromRawText(string rawText)
    {
        var parsed = EntryTextParser.Parse(rawText);
        return parsed.Status is { } entryStatus
            ? RepositoryBacklogText.ToKnowledgeStatus(entryStatus)
            : null;
    }

    private static List<string> BuildSegmentWithMeta(string rawText, string? status)
    {
        var lines = rawText.TrimEnd('\n').Split('\n').ToList();
        var result = new List<string>();

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

        for (var i = 0; i <= titleIndex; i++)
        {
            result.Add(lines[i]);
        }

        var metaLineIndex = titleIndex + 1;
        while (metaLineIndex < lines.Count && string.IsNullOrWhiteSpace(lines[metaLineIndex]))
        {
            metaLineIndex++;
        }

        var hasSigilMetaLine = metaLineIndex < lines.Count && EntryTextParser.IsMetadataLine(lines[metaLineIndex]);
        if (hasSigilMetaLine)
        {
            for (var i = titleIndex + 1; i < metaLineIndex; i++)
            {
                result.Add(lines[i]);
            }

            result.Add(lines[metaLineIndex]);
        }

        var bodyStart = hasSigilMetaLine ? metaLineIndex + 1 : titleIndex + 1;

        if (!string.IsNullOrWhiteSpace(status))
        {
            result.Add(string.Empty);
            result.Add("```meta");
            result.Add($"status: {status}");
            result.Add("```");
        }

        for (var i = bodyStart; i < lines.Count; i++)
        {
            result.Add(lines[i]);
        }

        if (result.Count > 0 && !string.IsNullOrWhiteSpace(result[^1]))
        {
            result.Add(string.Empty);
        }

        return result;
    }

    private static void UpsertStatusInRange(List<string> lines, int start, int end, string status)
    {
        for (var i = start; i < end && i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (!trimmed.Equals("```meta", StringComparison.OrdinalIgnoreCase)) continue;

            var close = i + 1;
            var statusLine = -1;
            while (close < end && close < lines.Count && !lines[close].TrimStart().StartsWith("```", StringComparison.Ordinal))
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

    private static void WriteAllText(string filePath, string newline, List<string> lines)
    {
        File.WriteAllText(filePath, string.Join(newline, lines));
    }
}
