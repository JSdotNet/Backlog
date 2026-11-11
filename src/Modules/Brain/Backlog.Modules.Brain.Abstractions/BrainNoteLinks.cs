using System.Text;
using System.Text.RegularExpressions;

namespace Backlog.Modules.Brain.Abstractions;

/// <summary>The two lists of a note's top <c>meta</c> block the app may append
/// to (local ADR 0052 §1).</summary>
public enum BrainLinkField
{
    /// <summary><c>tasks</c>: the ids of the tasks made from the note.</summary>
    Tasks,

    /// <summary><c>roadmap</c>: the Roadmap Tags of the plans made from it.</summary>
    Roadmap,
}

/// <summary>What <see cref="BrainNoteLinks.Append"/> made of a note's text: the
/// new text, the same text when the value was already there, or the reason it
/// refused.</summary>
public sealed record BrainLinkEdit(BrainLinkOutcome Outcome, string? Text, string? Error);

/// <summary>
/// The one edit the app makes to a note already in the Brain: one value appended
/// to the <c>tasks</c> or <c>roadmap</c> list of its top <c>meta</c> block
/// (<c>.devbook/domain/brain/domain.invariants.md#cross-linking</c>, local ADR
/// 0052). It is where the three Cross-Linking invariants are kept:
/// <list type="bullet">
/// <item><b>Only the two lists change.</b> The text is cut into lines with their own
/// line endings, and only the list's line — or one new line, an item after the
/// last item of a block list, or the field as the last line of the block — is
/// changed; every other line, ending and byte is put back as it was read. A flow
/// list (<c>[a, b]</c>) stays one and a block list (<c>- a</c>) stays one; a bare
/// <c>tasks:</c> with no items has no style yet and becomes a flow list, the one
/// the devbook parse reads.</item>
/// <item><b>A present value is not added again.</b> A value the list holds — a task
/// id in any case, a Roadmap Tag exactly — answers
/// <see cref="BrainLinkOutcome.AlreadyPresent"/> and the text unchanged.</item>
/// <item><b>An unparsable block is not written.</b> No title heading, no <c>meta</c>
/// block under it, a block never closed, a line that is neither a field, a list
/// item nor a comment, the field named twice, a value that is not a list, or a
/// flow list not closed on its line is refused with the reason, and no text is
/// made. This is stricter than the devbook parse, which skips a line it cannot
/// read: an edit into a block the app cannot read whole could land anywhere, so
/// the person repairs the block first (local ADR 0052 §4).</item>
/// </list>
/// <para>The top block is found the way the devbook parse finds a file's block
/// (<c>DevbookMarkdown.Parse</c> in <c>Backlog.Infrastructure.Devbook</c>): the
/// block after the first level-1 heading outside a fence, past blank lines, with
/// JavaScript's whitespace. This project references nothing, so the rules are
/// spelt out here, as <see cref="BrainNoteText"/> spells its own.</para>
/// </summary>
public static partial class BrainNoteLinks
{
    /// <summary>The field's key as the <c>meta</c> block spells it.</summary>
    public static string KeyOf(BrainLinkField field) => field switch
    {
        BrainLinkField.Tasks => "tasks",
        BrainLinkField.Roadmap => "roadmap",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    /// <summary>
    /// <paramref name="text"/> with <paramref name="value"/> appended to
    /// <paramref name="field"/>'s list in its top <c>meta</c> block, or the text
    /// unchanged when the list holds it already, or the reason it cannot be.
    /// </summary>
    public static BrainLinkEdit Append(string text, BrainLinkField field, string value)
    {
        ArgumentNullException.ThrowIfNull(text);

        var key = KeyOf(field);
        var item = JsTrim(value ?? string.Empty);
        if (item.Length == 0) return Refuse($"There is no value to add to `{key}`.");
        if (item.Any(c => c is ',' or '[' or ']' or '"' or '\'' || char.IsControl(c) || IsJsWhitespace(c)))
        {
            return Refuse($"`{item}` cannot be written into a `{key}` list: it holds a space, a comma, a bracket or a quote.");
        }

        var lines = Split(text);
        var block = FindBlock(lines);
        if (block.Error is { } blockError) return Refuse(blockError);

        var parsed = ParseBlock(lines, block.Open + 1, block.Close, key);
        if (parsed.Error is { } error) return Refuse(error);

        var comparer = field == BrainLinkField.Tasks ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        if (parsed.KeyLine < 0)
        {
            // A missing field is the block's last line, in the block's own ending.
            lines.Insert(block.Close, new BrainTextLine($"{key}: [{item}]", lines[block.Open].Ending));
            return Changed(lines);
        }

        var keyLine = lines[parsed.KeyLine];
        var raw = JsTrim(keyLine.Text[(keyLine.Text.IndexOf(':', StringComparison.Ordinal) + 1)..]);

        if (raw.Length == 0)
        {
            if (parsed.Items.Any(i => comparer.Equals(i.Value, item))) return Unchanged(text);

            if (parsed.Items.Count == 0)
            {
                // A field with no value and no items has no style yet: it becomes the
                // flow list the devbook parse reads, which does not read block items.
                var colon = keyLine.Text.IndexOf(':', StringComparison.Ordinal);
                lines[parsed.KeyLine] = keyLine with { Text = keyLine.Text[..(colon + 1)] + $" [{item}]" };
                return Changed(lines);
            }

            var last = parsed.Items[^1];
            var lastLine = lines[last.LineIndex];
            lines.Insert(last.LineIndex + 1, new BrainTextLine(lastLine.Text[..last.ValueAt] + item, lastLine.Ending));
            return Changed(lines);
        }

        if (!raw.StartsWith('[')) return Refuse($"`{key}` on line {parsed.KeyLine + 1} is not a list.");
        if (!raw.EndsWith(']')) return Refuse($"The `{key}` list on line {parsed.KeyLine + 1} is not closed on its line.");
        if (raw[1..^1].IndexOfAny(['[', ']']) >= 0) return Refuse($"The `{key}` list on line {parsed.KeyLine + 1} holds a bracket, so where it ends is unclear.");

        var entries = Entries(raw[1..^1]);
        if (entries.Any(e => comparer.Equals(e, item))) return Unchanged(text);

        var content = keyLine.Text;
        var close = content.LastIndexOf(']');
        var open = content.IndexOf('[', content.IndexOf(':', StringComparison.Ordinal));
        var at = AfterLastNonSpace(content, open + 1, close);
        var edited = entries.Count == 0
            ? content[..(open + 1)] + item + content[close..]
            : content[..at] + (content[at - 1] == ',' ? " " : ", ") + item + content[at..];

        lines[parsed.KeyLine] = keyLine with { Text = edited };
        return Changed(lines);
    }

    /// <summary>
    /// The values of the list field <paramref name="key"/> in the note's top
    /// <c>meta</c> block — a flow list or a block list, read as
    /// <see cref="Append"/> reads it — in their order. Empty when the field is
    /// missing, is not a list, or the block does not parse: a reader of the note's
    /// <c>repos</c> or <c>related</c> takes nothing from a block the writer would
    /// refuse.
    /// </summary>
    public static IReadOnlyList<string> ReadList(string text, string key)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var lines = Split(text);
        var block = FindBlock(lines);
        if (block.Error is not null) return [];

        var parsed = ParseBlock(lines, block.Open + 1, block.Close, key);
        if (parsed.Error is not null || parsed.KeyLine < 0) return [];

        var keyLine = lines[parsed.KeyLine].Text;
        var raw = JsTrim(keyLine[(keyLine.IndexOf(':', StringComparison.Ordinal) + 1)..]);
        if (raw.Length == 0) return [.. parsed.Items.Select(item => item.Value)];
        if (!raw.StartsWith('[') || !raw.EndsWith(']') || raw[1..^1].IndexOfAny(['[', ']']) >= 0) return [];

        return Entries(raw[1..^1]);
    }

    /// <summary>The entries of a flow list's inside, split as the devbook parse
    /// splits them — a comma inside a quoted entry is part of it — trimmed and
    /// unquoted, blanks dropped.</summary>
    private static List<string> Entries(string inner)
    {
        var entries = new List<string>();
        if (JsTrim(inner).Length == 0) return entries;

        var current = new StringBuilder();
        char? quote = null;
        foreach (var character in inner)
        {
            if (quote is { } openQuote)
            {
                if (character == openQuote) quote = null;
            }
            else if (character is '"' or '\'')
            {
                if (JsTrim(current.ToString()).Length == 0) quote = character;
            }
            else if (character == ',')
            {
                Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        Add(current.ToString());
        return entries;

        void Add(string entry)
        {
            var trimmed = StripQuotes(JsTrim(entry));
            if (trimmed.Length > 0) entries.Add(trimmed);
        }
    }

    private static int AfterLastNonSpace(string content, int from, int to)
    {
        var at = to;
        while (at > from && IsJsWhitespace(content[at - 1])) at--;
        return at;
    }

    /// <summary>The top block: the first level-1 heading outside a fence, then
    /// past blank lines a <c>```meta</c> line, and the <c>```</c> that closes it
    /// — or why there is none.</summary>
    private static (int Open, int Close, string? Error) FindBlock(List<BrainTextLine> lines)
    {
        string? fence = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var marker = FenceMarker().Match(lines[i].Text);
            if (fence is not null)
            {
                var closer = marker.Groups[1].Value;
                if (marker.Success && closer[0] == fence[0] && closer.Length >= fence.Length) fence = null;
                continue;
            }

            if (marker.Success)
            {
                fence = marker.Groups[1].Value;
                continue;
            }

            var heading = Heading().Match(lines[i].Text);
            if (!heading.Success || heading.Groups[1].Length != 1) continue;

            var j = i + 1;
            while (j < lines.Count && JsTrim(lines[j].Text).Length == 0) j++;
            if (j >= lines.Count || !MetaFence().IsMatch(JsTrim(lines[j].Text))) return (-1, -1, "The note has no meta block under its title.");

            var k = j + 1;
            while (k < lines.Count && JsTrim(lines[k].Text) != "```") k++;
            return k < lines.Count ? (j, k, null) : (-1, -1, "The note's top meta block is never closed.");
        }

        return (-1, -1, "The note has no title heading, so it has no top meta block.");
    }

    private sealed record BrainListEntry(int LineIndex, int ValueAt, string Value);

    private sealed record BrainBlockReading(int KeyLine, List<BrainListEntry> Items, string? Error);

    /// <summary>
    /// The block's lines, read strictly: each is blank, a <c>#</c> comment, a
    /// <c>key: value</c> field, or a <c>- item</c> under a field with no value of
    /// its own. Answers where <paramref name="key"/> is and the items under it, or
    /// the first line that is none of those.
    /// </summary>
    private static BrainBlockReading ParseBlock(List<BrainTextLine> lines, int from, int to, string key)
    {
        var keyLine = -1;
        var items = new List<BrainListEntry>();
        string? openKey = null;

        for (var i = from; i < to; i++)
        {
            var line = lines[i].Text;
            var trimmed = JsTrim(line);
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;

            if (trimmed[0] == '-' && (trimmed.Length == 1 || IsJsWhitespace(trimmed[1])))
            {
                if (openKey is null) return new BrainBlockReading(-1, items, $"Line {i + 1} of the note's meta block is a list item under no field.");
                if (openKey != key) continue;

                var dash = line.IndexOf('-', StringComparison.Ordinal);
                var at = dash + 1;
                while (at < line.Length && IsJsWhitespace(line[at])) at++;
                if (at == dash + 1) return new BrainBlockReading(-1, items, $"Line {i + 1} of the note's meta block is an empty list item.");
                var entry = StripQuotes(JsTrim(line[at..]));
                if (entry.Length == 0) return new BrainBlockReading(-1, items, $"Line {i + 1} of the note's meta block is an empty list item.");
                items.Add(new BrainListEntry(i, at, entry));
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var name = colon < 0 ? string.Empty : JsTrim(line[..colon]);
            if (name.Length == 0) return new BrainBlockReading(-1, items, $"Line {i + 1} of the note's meta block is neither a field nor a list item.");

            var value = JsTrim(line[(colon + 1)..]);
            if (name == key)
            {
                if (keyLine >= 0) return new BrainBlockReading(-1, items, $"The note's meta block names `{key}` twice, on lines {keyLine + 1} and {i + 1}.");
                if (value.StartsWith('[') && !value.EndsWith(']')) return new BrainBlockReading(-1, items, $"The `{key}` list on line {i + 1} is not closed on its line.");
                keyLine = i;
            }

            openKey = value.Length == 0 ? name : null;
        }

        return new BrainBlockReading(keyLine, items, null);
    }

    private static BrainLinkEdit Refuse(string error) => new(BrainLinkOutcome.Refused, null, error);

    private static BrainLinkEdit Unchanged(string text) => new(BrainLinkOutcome.AlreadyPresent, text, null);

    private static BrainLinkEdit Changed(List<BrainTextLine> lines)
    {
        var text = new StringBuilder();
        foreach (var line in lines) text.Append(line.Text).Append(line.Ending);
        return new BrainLinkEdit(BrainLinkOutcome.Appended, text.ToString(), null);
    }

    private sealed record BrainTextLine(string Text, string Ending);

    /// <summary>The text cut where <c>split(/\r?\n/)</c> cuts it, each line keeping
    /// the ending it had, so joining them back is the text byte for byte.</summary>
    private static List<BrainTextLine> Split(string text)
    {
        var lines = new List<BrainTextLine>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;

            var crlf = i > start && text[i - 1] == '\r';
            lines.Add(new BrainTextLine(text[start..(crlf ? i - 1 : i)], crlf ? "\r\n" : "\n"));
            start = i + 1;
        }

        lines.Add(new BrainTextLine(text[start..], string.Empty));
        return lines;
    }

    private static string StripQuotes(string value) =>
        value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;

    /// <summary><c>String.prototype.trim</c>.</summary>
    private static string JsTrim(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && IsJsWhitespace(value[start])) start++;
        while (end > start && IsJsWhitespace(value[end - 1])) end--;
        return value[start..end];
    }

    private static bool IsJsWhitespace(char c) =>
        (int)c is 0x09 or 0x0a or 0x0b or 0x0c or 0x0d or 0x20 or 0xa0 or 0x1680
            or (>= 0x2000 and <= 0x200a)
            or 0x2028 or 0x2029 or 0x202f or 0x205f or 0x3000 or 0xfeff;

    private const string JsWhitespace = "\\t\\n\\v\\f\\r \\u00a0\\u1680\\u2000-\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000\\ufeff";

    [GeneratedRegex("^[" + JsWhitespace + "]*(`{3,}|~{3,})")]
    private static partial Regex FenceMarker();

    [GeneratedRegex("^(#{1,6})[" + JsWhitespace + "]+([^\\n\\r\\u2028\\u2029]*)\\z")]
    private static partial Regex Heading();

    [GeneratedRegex("^```meta[" + JsWhitespace + "]*$")]
    private static partial Regex MetaFence();
}
