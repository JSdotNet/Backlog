using Backlog.SharedKernel.Markdown;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// The gate's handoff laid out for a phone: a line that opens a paragraph, or a line of it,
/// with a label the flow-runner writes — <c>Acceptance criteria:</c>, <c>Criteria:</c>,
/// <c>What to check:</c>, <c>Evidence:</c> or <c>Open:</c> — starts a block that becomes the
/// label in bold and a list of its <c>;</c>-separated clauses, one a line
/// (<c>.devbook/domain/notifications/requirements.md#requirement-a-run-at-personal-validation-asks-to-be-approved</c>).
/// <para>
/// The criteria and what to check are a checklist the reviewer reads against; the evidence
/// and its open points are plain bullets, because an empty box would read as "not done". An
/// <c>Open:</c> inside the evidence, after a sentence's end, is lifted out into its own list.
/// Each clause loses one closing full stop, so the items of a list end alike.
/// </para>
/// <para>
/// The labels are written ad hoc, so it changes only what it recognises: a <c>;</c> inside
/// parentheses, brackets, emphasis, a code span, an autolink or a bare address does not
/// split; a paragraph without a labelled line, one already written as a list, and anything
/// in a fenced block are left byte for byte as they were — so a handoff it already laid out
/// is unchanged. It is a presentation of the text only: the run file keeps the handoff as the
/// gate passed it.
/// </para>
/// </summary>
internal static class HandoffText
{
    private const string OpenLabel = "Open";

    /// <summary>The labels that open a block, longest first so <c>Acceptance criteria</c>
    /// is not read as <c>Criteria</c>, and whether their clauses are a checklist.</summary>
    private static readonly (string Label, bool Checklist)[] Labels =
    [
        ("Acceptance criteria", true),
        ("What to check", true),
        ("Criteria", true),
        ("Evidence", false),
        (OpenLabel, false)
    ];

    /// <summary>
    /// <paramref name="handoff"/> with each labelled block laid out as its label and a list;
    /// every other line exactly as it was.
    /// </summary>
    internal static string Structure(string handoff)
    {
        var lines = handoff.Split('\n');
        var output = new List<string>(lines.Length);
        MarkdownFence? fence = null;
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            if (fence is { } open)
            {
                if (open.IsClosedBy(line)) fence = null;
                output.Add(line);
                i++;
                continue;
            }

            if (MarkdownFence.Open(line) is { } opened)
            {
                fence = opened;
                output.Add(line);
                i++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                output.Add(line);
                i++;
                continue;
            }

            // A paragraph runs to the next blank line or fence.
            var start = i;
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && MarkdownFence.Open(lines[i]) is null) i++;
            var paragraph = lines[start..i];

            if (Laid(paragraph) is { } laid) output.AddRange(laid);
            else output.AddRange(paragraph);
        }

        return string.Join('\n', output);
    }

    /// <summary>
    /// The paragraph with each block that a labelled line starts laid out, the lines before
    /// the first such block kept as written and set apart by a blank line; or null when no
    /// block was laid out, or the paragraph already holds a list, heading, quote or table
    /// line.
    /// </summary>
    private static List<string>? Laid(string[] paragraph)
    {
        if (paragraph.Any(line => IsListLine(line) || line.TrimStart() is ['#' or '>' or '|', ..])) return null;

        var starts = Enumerable.Range(0, paragraph.Length).Where(at => Labelled(paragraph[at].Trim()) is not null).ToList();
        if (starts.Count == 0) return null;

        var parts = new List<(IReadOnlyList<string> Lines, bool Laid)>();
        if (starts[0] > 0) parts.Add((paragraph[..starts[0]], false));

        for (var k = 0; k < starts.Count; k++)
        {
            var block = paragraph[starts[k]..(k + 1 < starts.Count ? starts[k + 1] : paragraph.Length)];
            parts.Add(Block(block) is { } laid ? (laid, true) : (block, false));
        }

        if (!parts.Any(part => part.Laid)) return null;

        var output = new List<string>();
        for (var k = 0; k < parts.Count; k++)
        {
            if (k > 0 && (parts[k].Laid || parts[k - 1].Laid)) output.Add(string.Empty);
            output.AddRange(parts[k].Lines);
        }

        return output;
    }

    /// <summary>The labelled block as label and list, an <c>Open:</c> in the evidence as a
    /// second list, or null when no clause follows the label.</summary>
    private static List<string>? Block(string[] block)
    {
        var text = string.Join(' ', block.Select(line => line.Trim()));
        if (Labelled(text) is not var (label, checklist, rest)) return null;

        var tail = default((string Label, string Text)?);
        if (!checklist && !string.Equals(label, OpenLabel, StringComparison.OrdinalIgnoreCase) && OpenAt(rest) is var split and >= 0)
        {
            tail = (rest.Substring(split, OpenLabel.Length), rest[(split + OpenLabel.Length + 1)..]);
            rest = rest[..split];
        }

        var clauses = Clauses(rest);
        if (clauses.Count == 0) return null;

        var laid = new List<string>();
        AppendList(laid, label, checklist, clauses);

        if (tail is { } open && Clauses(open.Text) is { Count: > 0 } points)
        {
            laid.Add(string.Empty);
            AppendList(laid, open.Label, checklist: false, points);
        }

        return laid;
    }

    private static void AppendList(List<string> laid, string label, bool checklist, List<string> clauses)
    {
        laid.Add($"**{label}**");
        laid.Add(string.Empty);
        foreach (var clause in clauses) laid.Add(checklist ? $"- [ ] {clause}" : $"- {clause}");
    }

    /// <summary>The label <paramref name="text"/> opens with, as written; whether its clauses
    /// are a checklist; and the text after its colon.</summary>
    private static (string Label, bool Checklist, string Text)? Labelled(string text)
    {
        foreach (var (label, checklist) in Labels)
        {
            if (text.Length > label.Length
                && text.StartsWith(label, StringComparison.OrdinalIgnoreCase)
                && text[label.Length] == ':')
            {
                return (text[..label.Length], checklist, text[(label.Length + 1)..]);
            }
        }

        return null;
    }

    /// <summary>Where an <c>Open:</c>, in any case, starts a sentence of
    /// <paramref name="text"/> at depth zero — after <c>.</c>, <c>?</c> or <c>!</c> and
    /// whitespace — or -1.</summary>
    private static int OpenAt(string text)
    {
        foreach (var (index, _, depth) in Tokens(text))
        {
            if (depth > 0 || index < 2 || !char.IsWhiteSpace(text[index - 1])) continue;
            if (string.Compare(text, index, OpenLabel + ":", 0, OpenLabel.Length + 1, StringComparison.OrdinalIgnoreCase) != 0) continue;

            var before = index - 1;
            while (before > 0 && char.IsWhiteSpace(text[before])) before--;

            if (text[before] is '.' or '?' or '!') return index;
        }

        return -1;
    }

    /// <summary>
    /// The <c>;</c>-separated clauses of <paramref name="text"/> at depth zero, trimmed, each
    /// without one closing full stop, the empty ones dropped. Clauses an emphasis span
    /// crosses are joined again; where a span never closes, its clauses stay apart.
    /// </summary>
    private static List<string> Clauses(string text)
    {
        var tokens = Tokens(text);
        var cuts = tokens.Where(token => token.Depth == 0 && token.Character == ';').Select(token => token.Index).ToList();

        var pieces = new List<(int Start, int End)>();
        var start = 0;
        foreach (var cut in cuts)
        {
            pieces.Add((start, cut));
            start = cut + 1;
        }

        pieces.Add((start, text.Length));

        var stars = DelimiterRuns(text, tokens, '*', minimum: 1);
        var tildes = DelimiterRuns(text, tokens, '~', minimum: 2);
        bool Open(int from, int to) =>
            stars.Count(at => at >= from && at < to) % 2 == 1 || tildes.Count(at => at >= from && at < to) % 2 == 1;

        var clauses = new List<(int Start, int End)>();
        var next = 0;
        while (next < pieces.Count)
        {
            var last = next;
            while (Open(pieces[next].Start, pieces[last].End) && last + 1 < pieces.Count) last++;

            if (Open(pieces[next].Start, pieces[last].End))
            {
                // Unbalanced to the end: the span is text, so its clauses are split as written.
                clauses.AddRange(pieces[next..]);
                break;
            }

            clauses.Add((pieces[next].Start, pieces[last].End));
            next = last + 1;
        }

        return [.. clauses.Select(clause => Tidy(text[clause.Start..clause.End])).Where(clause => clause.Length > 0)];
    }

    private static string Tidy(string clause)
    {
        var trimmed = clause.Trim();
        return trimmed.EndsWith('.') && !trimmed.EndsWith("..", StringComparison.Ordinal) ? trimmed[..^1].TrimEnd() : trimmed;
    }

    /// <summary>Where each run of <paramref name="marker"/> at least
    /// <paramref name="minimum"/> long starts among <paramref name="tokens"/> — skipping a run
    /// with whitespace on both sides, which emphasises nothing.</summary>
    private static List<int> DelimiterRuns(string text, List<(int Index, char Character, int Depth)> tokens, char marker, int minimum)
    {
        var runs = new List<int>();
        var k = 0;

        while (k < tokens.Count)
        {
            if (tokens[k].Character != marker)
            {
                k++;
                continue;
            }

            var first = tokens[k].Index;
            var end = k;
            while (end + 1 < tokens.Count && tokens[end + 1].Character == marker && tokens[end + 1].Index == tokens[end].Index + 1) end++;

            var after = tokens[end].Index + 1;
            var spaced = (first == 0 || char.IsWhiteSpace(text[first - 1])) && (after >= text.Length || char.IsWhiteSpace(text[after]));
            if (end - k + 1 >= minimum && !spaced) runs.Add(first);

            k = end + 1;
        }

        return runs;
    }

    /// <summary>
    /// The characters of <paramref name="text"/> that can carry structure, each with its
    /// bracket depth: everything outside a code span, an autolink, a bare address and a
    /// backslash escape. Only a <c>(</c> or <c>[</c> with its closer raises the depth; an
    /// opener that never closes, or a closer with no opener, is text. A plain scan, so no
    /// input can make it backtrack.
    /// </summary>
    private static List<(int Index, char Character, int Depth)> Tokens(string text)
    {
        var visible = new List<(int Index, char Character)>();
        var i = 0;

        while (i < text.Length)
        {
            var character = text[i];

            if (character == '\\' && i + 1 < text.Length)
            {
                i += 2;
                continue;
            }

            if (character == '`')
            {
                var run = Run(text, i);
                var close = ClosingRun(text, i + run, run);
                i = close >= 0 ? close + run : i + run;
                continue;
            }

            if (character == '<' && AutolinkEnd(text, i) is var autolink and >= 0)
            {
                i = autolink + 1;
                continue;
            }

            if (AddressEnd(text, i) is var address and > 0)
            {
                i = address;
                continue;
            }

            visible.Add((i, character));
            i++;
        }

        var matched = new HashSet<int>();
        var openers = new Stack<(int At, char Opener)>();

        for (var k = 0; k < visible.Count; k++)
        {
            switch (visible[k].Character)
            {
                case '(' or '[':
                    openers.Push((k, visible[k].Character));
                    break;
                case ')' or ']':
                    var opener = visible[k].Character == ')' ? '(' : '[';
                    if (openers.Count > 0 && openers.Peek().Opener == opener)
                    {
                        matched.Add(openers.Pop().At);
                        matched.Add(k);
                    }

                    break;
            }
        }

        var tokens = new List<(int Index, char Character, int Depth)>(visible.Count);
        var depth = 0;

        for (var k = 0; k < visible.Count; k++)
        {
            var (index, character) = visible[k];

            if (matched.Contains(k) && character is ')' or ']') depth--;
            tokens.Add((index, character, depth));
            if (matched.Contains(k) && character is '(' or '[') depth++;
        }

        return tokens;
    }

    /// <summary>Where the <c>&gt;</c> that closes an autolink opened at
    /// <paramref name="at"/> is — before any whitespace — or -1.</summary>
    private static int AutolinkEnd(string text, int at)
    {
        var end = at + 1;
        while (end < text.Length && text[end] != '>' && !char.IsWhiteSpace(text[end])) end++;

        return end < text.Length && text[end] == '>' && end > at + 1 ? end : -1;
    }

    /// <summary>
    /// Where a bare <c>http://</c> or <c>https://</c> address starting at
    /// <paramref name="at"/> ends, exclusive, or -1: at whitespace, at a <c>;</c> that ends
    /// the clause (whitespace or the end after it), or at a <c>)</c> or <c>]</c> it did not
    /// open.
    /// </summary>
    private static int AddressEnd(string text, int at)
    {
        if (at > 0 && char.IsLetterOrDigit(text[at - 1])) return -1;
        if (!Starts(text, at, "http://") && !Starts(text, at, "https://")) return -1;

        var depth = 0;
        var end = at;

        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            var character = text[end];

            if (character is '(' or '[')
            {
                depth++;
            }
            else if (character is ')' or ']')
            {
                if (depth == 0) break;
                depth--;
            }
            else if (character == ';' && (end + 1 == text.Length || char.IsWhiteSpace(text[end + 1])))
            {
                break;
            }

            end++;
        }

        return end;

        static bool Starts(string text, int at, string scheme) =>
            string.Compare(text, at, scheme, 0, scheme.Length, StringComparison.OrdinalIgnoreCase) == 0
            && text.Length - at > scheme.Length;
    }

    private static int Run(string text, int at)
    {
        var end = at;
        while (end < text.Length && text[end] == '`') end++;
        return end - at;
    }

    /// <summary>Where the next backtick run exactly <paramref name="length"/> long starts,
    /// from <paramref name="from"/>, or -1.</summary>
    private static int ClosingRun(string text, int from, int length)
    {
        var i = from;

        while (i < text.Length)
        {
            if (text[i] != '`')
            {
                i++;
                continue;
            }

            var run = Run(text, i);
            if (run == length) return i;
            i += run;
        }

        return -1;
    }

    /// <summary>A bullet, checklist or numbered line, by the markers MarkdownPreview reads:
    /// <c>-</c> or <c>*</c>, or digits and <c>.</c> or <c>)</c>, then whitespace.</summary>
    private static bool IsListLine(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length < 2) return false;
        if (trimmed[0] is '-' or '*' && trimmed[1] is ' ' or '\t') return true;

        var digits = 0;
        while (digits < trimmed.Length && char.IsAsciiDigit(trimmed[digits])) digits++;

        return digits > 0
            && digits + 1 < trimmed.Length
            && trimmed[digits] is '.' or ')'
            && trimmed[digits + 1] is ' ' or '\t';
    }
}
