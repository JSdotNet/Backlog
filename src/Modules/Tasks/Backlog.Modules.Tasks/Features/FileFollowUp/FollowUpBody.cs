using System.Text.RegularExpressions;

namespace Backlog.Modules.Tasks.Features.FileFollowUp;

/// <summary>
/// The person's words as a follow-up's body: kept as plain text, never read back as
/// structure (<c>.devbook/domain/tasks/requirements.md#filing-a-follow-up-after-an-entry</c>).
/// <para>
/// Escaped, never refused: the words are an answer the person already sent from a
/// phone or a notification card, and there is nobody to tell that a line of it looked
/// like a step. A Markdown backslash goes in front of exactly what the entry grammar
/// (<c>EntryTextParser</c>) would otherwise read — a heading, which is also a sub-item
/// or a second entry; a checklist line, which is a sub-item; a fence, which hides and
/// shows structure; a backtick-only line, which reads as a metadata line; and a
/// <c>#tag</c> or <c>+tag</c> not welded to the word before it. Ordinary prose —
/// <c>C#</c>, <c>2+2</c>, a hyphen mid-line — is left exactly as typed.
/// </para>
/// </summary>
internal static partial class FollowUpBody
{
    /// <summary>The words, line endings normalised and trimmed, each line escaped
    /// where the grammar would read it as more than text.</summary>
    public static string From(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim().Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = Escape(lines[i]);
        }

        return string.Join('\n', lines);
    }

    private static string Escape(string line)
    {
        var indent = line.Length - line.TrimStart().Length;
        var lead = line[..indent];
        var rest = line[indent..];

        // A line of backtick tokens: every backtick is escaped, so the line is no
        // metadata line to the grammar and no row of code spans to the read view —
        // escaping the first alone would leave "` `" between two tokens as a span.
        if (MetadataLine().IsMatch(rest)) return lead + rest.Replace("`", "\\`", StringComparison.Ordinal);

        // A fence: the opening backtick (or tilde) is escaped, so the line no longer
        // starts the way the grammar looks for, and what is left is too short to open
        // a code span or a strikethrough.
        if (rest.StartsWith("```", StringComparison.Ordinal) || rest.StartsWith("~~~", StringComparison.Ordinal))
        {
            return lead + "\\" + rest;
        }

        // A list item that opens with a checkbox: the marker is escaped, so the line
        // is a paragraph that reads "- [ ] …" rather than a step.
        if (ChecklistLine().IsMatch(rest)) rest = "\\" + rest;

        // A heading — any level, which also covers the `# ` that would start a second
        // entry — and every tag token: each `#` or `+` that opens a word with nothing
        // welded to its left.
        return lead + TagSigil().Replace(rest, "\\$1");
    }

    [GeneratedRegex(@"^(\s*`[^`\n]+`\s*)+$")]
    private static partial Regex MetadataLine();

    [GeneratedRegex(@"^[-*+][ \t]+\[( |x|X)\]")]
    private static partial Regex ChecklistLine();

    /// <summary>A <c>#</c> at the start of the text or after whitespace — a heading
    /// marker or a general tag — or a <c>+</c> there that opens a word, a plan tag.</summary>
    [GeneratedRegex(@"(?<!\S)(#|\+(?=[A-Za-z]))")]
    private static partial Regex TagSigil();
}
