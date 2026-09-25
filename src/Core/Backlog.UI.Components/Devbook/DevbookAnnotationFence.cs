namespace Backlog.UI.Components.Devbook;

/// <summary>
/// One <c>annotation</c> fence: a review note the repository keeps in the chapter
/// itself, beside the passage it is about, per the <c>devbook</c> plugin's
/// <c>devbook-annotations.md</c>.
///
/// <para><b>Not the remark.</b> Local ADR 0011 made the person's own remark on a
/// chapter block — <c>DevbookAnnotation</c>, kept in the app and replicated through
/// the third container — a different thing from this fence, and the two are never
/// merged. The fence is shared review state in a tracked file; the remark is
/// private data outside it. This type only reads the fence.</para>
///
/// <para><b>Never written from C#.</b> <c>annotations.mjs</c> is the fence's only
/// writer, by that rule's own terms, so there is no serialiser here.</para>
///
/// <para><b>Not chapter content.</b> A reader loading a chapter for context skips
/// every fence; a view shows it as a note, or skips it deliberately.</para>
/// </summary>
public sealed record DevbookAnnotationFence
{
    /// <summary>The fence language that marks a note.</summary>
    public const string FenceLanguage = "annotation";

    /// <summary>The closed set of kinds, default first.</summary>
    public static IReadOnlyList<string> Kinds { get; } = ["comment", "question", "suggestion", "flag"];

    /// <summary>The two states, default first.</summary>
    public static IReadOnlyList<string> Statuses { get; } = ["open", "resolved"];

    public string? Author { get; init; }

    /// <summary>The day the note was made, as authored (<c>YYYY-MM-DD</c>).</summary>
    public string? Date { get; init; }

    /// <summary>Markdown.</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>One of <see cref="Kinds"/> as authored, or <c>comment</c> when the
    /// field is absent.</summary>
    public string Kind { get; init; } = "comment";

    /// <summary>One of <see cref="Statuses"/> as authored, or <c>open</c> when the
    /// field is absent.</summary>
    public string Status { get; init; } = "open";

    /// <summary>The phrase in the block above that the note is about. For a
    /// person's eye only — never for resolution.</summary>
    public string? Quote { get; init; }

    public IReadOnlyList<DevbookAnnotationReply> Replies { get; init; } = [];

    /// <summary>Whether the note is still waiting on someone.</summary>
    public bool IsOpen => !string.Equals(Status, "resolved", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the kind is in the closed set. An unknown kind still reads
    /// back as authored; a view may flag it.</summary>
    public bool HasKnownKind => Kinds.Contains(Kind, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a fence opened a note.</summary>
    public static bool IsAnnotationBlock(string? language) =>
        language is not null && language.Trim().Equals(FenceLanguage, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the text inside the fence. Forgiving, like the <c>meta</c> reader: a
    /// note missing a required field still reads back with what it has, because a
    /// viewer is not the gate that refuses it.
    ///
    /// <para>The grammar is the rule's small YAML subset — top-level scalars, a
    /// <c>|</c> block scalar for a body that runs past a line, a <c>replies</c>
    /// list of <c>{author, date, body}</c>, and an <c>ext</c> mapping that is
    /// opaque here and skipped.</para>
    /// </summary>
    public static DevbookAnnotationFence Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return new DevbookAnnotationFence();

        var lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var replies = new List<DevbookAnnotationReply>();

        var index = 0;
        while (index < lines.Length)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line) || Indent(line) > 0)
            {
                index++;
                continue;
            }

            if (!TrySplit(line.Trim(), out var key, out var value))
            {
                index++;
                continue;
            }

            index++;

            if (key.Equals("replies", StringComparison.OrdinalIgnoreCase))
            {
                replies.AddRange(ReadReplies(lines, ref index));
                continue;
            }

            if (key.Equals("ext", StringComparison.OrdinalIgnoreCase))
            {
                // Namespaced extension state: opaque, validated only as a
                // mapping, and nobody's business here. Skip its indented body.
                while (index < lines.Length && (string.IsNullOrWhiteSpace(lines[index]) || Indent(lines[index]) > 0)) index++;
                continue;
            }

            fields[key] = value == "|" ? ReadBlockScalar(lines, ref index, 0) : Unquote(value);
        }

        return new DevbookAnnotationFence
        {
            Author = Field(fields, "author"),
            Date = Field(fields, "date"),
            Body = Field(fields, "body") ?? string.Empty,
            Kind = Field(fields, "kind") ?? "comment",
            Status = Field(fields, "status") ?? "open",
            Quote = Field(fields, "quote"),
            Replies = replies
        };
    }

    private static List<DevbookAnnotationReply> ReadReplies(string[] lines, ref int index)
    {
        var replies = new List<DevbookAnnotationReply>();
        Dictionary<string, string>? current = null;
        var itemIndent = -1;

        while (index < lines.Length)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                index++;
                continue;
            }

            var indent = Indent(line);
            if (indent == 0) break;

            var text = line.Trim();
            if (text.StartsWith("- ", StringComparison.Ordinal) || text == "-")
            {
                if (current is not null) replies.Add(Reply(current));
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                itemIndent = indent + 2;
                text = text.Length > 1 ? text[2..].Trim() : string.Empty;
                index++;
                if (text.Length == 0) continue;
            }
            else
            {
                index++;
            }

            if (current is null || !TrySplit(text, out var key, out var value)) continue;

            current[key] = value == "|" ? ReadBlockScalar(lines, ref index, itemIndent) : Unquote(value);
        }

        if (current is not null) replies.Add(Reply(current));
        return replies;
    }

    private static DevbookAnnotationReply Reply(Dictionary<string, string> fields) =>
        new(Field(fields, "author"), Field(fields, "date"), Field(fields, "body") ?? string.Empty);

    /// <summary>The lines of a <c>|</c> block, dedented by the first line's
    /// indentation, ending at the first non-blank line indented no deeper than the
    /// key that owns it.</summary>
    private static string ReadBlockScalar(string[] lines, ref int index, int ownerIndent)
    {
        var collected = new List<string>();
        var dedent = -1;

        while (index < lines.Length)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                collected.Add(string.Empty);
                index++;
                continue;
            }

            var indent = Indent(line);
            if (indent <= ownerIndent) break;

            if (dedent < 0) dedent = indent;
            collected.Add(line.Length >= dedent ? line[Math.Min(dedent, indent)..] : line.TrimStart());
            index++;
        }

        while (collected.Count > 0 && collected[^1].Length == 0) collected.RemoveAt(collected.Count - 1);
        return string.Join('\n', collected);
    }

    private static bool TrySplit(string text, out string key, out string value)
    {
        var separator = text.IndexOf(':');
        if (separator <= 0)
        {
            key = value = string.Empty;
            return false;
        }

        key = text[..separator].Trim();
        value = text[(separator + 1)..].Trim();
        return key.Length > 0;
    }

    private static int Indent(string line)
    {
        var count = 0;
        while (count < line.Length && (line[count] == ' ' || line[count] == '\t')) count++;
        return count;
    }

    private static string? Field(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string Unquote(string value)
    {
        var text = value.Trim();
        if (text.Length >= 2 && ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\'')))
        {
            text = text[1..^1];
        }

        return text;
    }
}

/// <summary>One reply inside a note's thread.</summary>
public sealed record DevbookAnnotationReply(string? Author, string? Date, string Body);
