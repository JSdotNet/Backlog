using System.Text.RegularExpressions;

namespace Backlog.Desktop.UI.Services;

internal static class KnowledgeMarkdownStatusWriter
{
    private static readonly Regex Heading = new("^(#{1,6})[ \\t]+(.+?)\\s*$", RegexOptions.Compiled);

    public static void UpdateStatus(string folderRoot, string itemPath, string folderPrefix, string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

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
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        var headingIndex = FindHeading(lines, anchor);
        if (headingIndex < 0) throw new InvalidOperationException($"Knowledge item heading was not found: {itemPath}");

        UpsertStatus(lines, headingIndex, status.Trim().ToLowerInvariant());
        File.WriteAllText(filePath, string.Join(newline, lines));
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

    private static void UpsertStatus(List<string> lines, int headingIndex, string status)
    {
        var index = headingIndex + 1;
        while (index < lines.Count && string.IsNullOrWhiteSpace(lines[index])) index++;

        if (index < lines.Count && string.Equals(lines[index].Trim(), "```meta", StringComparison.OrdinalIgnoreCase))
        {
            var close = index + 1;
            var statusLine = -1;
            while (close < lines.Count && !lines[close].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (lines[close].TrimStart().StartsWith("status:", StringComparison.OrdinalIgnoreCase)) statusLine = close;
                close++;
            }

            if (statusLine >= 0)
            {
                var indent = lines[statusLine][..(lines[statusLine].Length - lines[statusLine].TrimStart().Length)];
                lines[statusLine] = $"{indent}status: {status}";
            }
            else
            {
                lines.Insert(index + 1, $"status: {status}");
            }
            return;
        }

        lines.InsertRange(headingIndex + 1, [string.Empty, "```meta", $"status: {status}", "```"]);
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
