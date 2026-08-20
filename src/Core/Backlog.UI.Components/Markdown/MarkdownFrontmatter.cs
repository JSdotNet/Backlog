using Backlog.UI.Components.Knowledge;

namespace Backlog.UI.Components.Markdown;

/// <summary>
/// The YAML frontmatter a markdown file may open with, and the body underneath
/// it.
///
/// <para>An instruction file states what it is about and where it applies in
/// four lines above its first heading. Read as markdown those lines are noise —
/// the parser sees a <c>---</c> divider and a paragraph of run-together
/// <c>key: value</c> pairs — so a reader is shown the file's plumbing before its
/// prose. Read here they are facts a host can draw as facts.</para>
///
/// <para>Three keys are lifted out by name because they are drawn as more than
/// text: <see cref="Description"/> as prose, <see cref="ApplyTo"/> and
/// <see cref="Tools"/> as lists. Every other key the file states comes back
/// through <see cref="Other"/>, in the order the file wrote it. Nothing is
/// dropped, and that is a rule rather than a courtesy: a viewer that hid a key it
/// had no field for would take a line out of the file and put it nowhere, and
/// whoever wrote that line would have no way to tell whether the file or the
/// viewer was wrong.</para>
///
/// <para>This is not a YAML parser and is not becoming one: it finds the leading
/// <c>---</c> line and the next one, and hands what is between them to
/// <see cref="KnowledgeMeta.ReadFields"/> — the same reader the <c>meta</c> fence
/// uses, because frontmatter is written in the same three shapes.</para>
///
/// <para>A file with no frontmatter, an unterminated block, or a block that
/// states nothing at all comes back as <see cref="None"/> with the text exactly
/// as it was. That is the invariant a caller leans on: the block leaves
/// <see cref="Body"/> only when this record has something to show in its place,
/// so <see cref="IsEmpty"/> answers "draw nothing" and "change nothing" at
/// once.</para>
/// </summary>
public sealed record MarkdownFrontmatter
{
    /// <summary>The fence line, exactly. <c>----</c> is a divider somebody drew,
    /// not a frontmatter delimiter.</summary>
    private const string Fence = "---";

    /// <summary>The keys with a field of their own here, lower-cased the way the
    /// shared reader hands keys back.</summary>
    private static readonly HashSet<string> Named = new(StringComparer.Ordinal)
    {
        "description", "applyto", "tools"
    };

    /// <summary>A file that stated nothing. <see cref="Body"/> is the text it was
    /// read from, untouched.</summary>
    public static MarkdownFrontmatter None { get; } = new();

    /// <summary>What the file says it is about, as prose. Unquoted in most files
    /// and quoted in some; either way it reads back the same.</summary>
    public string? Description { get; init; }

    /// <summary>The globs a path-specific instruction file applies to, one entry
    /// per glob.</summary>
    public IReadOnlyList<string> ApplyTo { get; init; } = [];

    /// <summary>The tools a prompt or chat mode declares.</summary>
    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>
    /// Every other key the block states, in the order it was written.
    ///
    /// <para><c>name</c> on a skill file, <c>mode</c> and <c>model</c> on a
    /// prompt: keys this record has nothing particular to say about, and which a
    /// viewer still has to show, because the block is taken out of the body the
    /// moment anything in it is drawn.</para>
    /// </summary>
    public IReadOnlyList<MarkdownFrontmatterField> Other { get; init; } = [];

    /// <summary>The file without its frontmatter — or the whole file when there
    /// was none to take out. Verbatim either way: line endings, trailing
    /// newline and all, because a caller may be showing this text beside the
    /// file it came from.</summary>
    public string? Body { get; init; }

    /// <summary>Whether the file stated any frontmatter at all. A file that did
    /// not should not leave a strip where the strip would have been — and keeps
    /// every line it arrived with, per the invariant above.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Description)
        && ApplyTo.Count == 0
        && Tools.Count == 0
        && Other.Count == 0;

    /// <summary>Reads a file's leading frontmatter block, if it has one.</summary>
    public static MarkdownFrontmatter Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return None with { Body = text };

        // Split on '\n' and joined back on '\n', so a CRLF file round-trips: the
        // '\r' stays on the end of its own line and the fence test trims it off.
        // Normalising the whole text instead would rewrite every line ending in a
        // file this only means to take four lines off the top of.
        var lines = text.Split('\n');

        // The block has to be the file's first line. A divider further down is a
        // divider, and a file that opens with prose has no frontmatter at all.
        if (!IsFence(lines[0])) return None with { Body = text };

        var close = -1;
        for (var index = 1; index < lines.Length; index++)
        {
            if (!IsFence(lines[index])) continue;

            close = index;
            break;
        }

        // Unterminated. Reading on to the end of the file would swallow the whole
        // document as metadata on the strength of one line.
        if (close < 0) return None with { Body = text };

        var block = string.Join('\n', lines[1..close]);
        var fields = KnowledgeMeta.ReadFields(block);

        var frontmatter = new MarkdownFrontmatter
        {
            // Lower-cased keys: that is what the shared reader hands back.
            Description = Scalar(fields, "description"),
            ApplyTo = Globs(fields.GetValueOrDefault("applyto", [])),
            Tools = fields.GetValueOrDefault("tools", []),
            Other = OtherFields(block, fields)
        };

        // Nothing stated means nothing to show, and a block nobody is showing
        // stays where its author put it.
        return frontmatter.IsEmpty
            ? None with { Body = text }
            : frontmatter with { Body = string.Join('\n', lines[(close + 1)..]) };
    }

    private static bool IsFence(string line) => line.Trim().Equals(Fence, StringComparison.Ordinal);

    private static string? Scalar(IReadOnlyDictionary<string, IReadOnlyList<string>> fields, string key) =>
        fields.TryGetValue(key, out var values) && values.Count > 0 ? values[0] : null;

    /// <summary>
    /// One entry per glob, which means splitting on commas here rather than in
    /// the shared reader.
    ///
    /// <para>This repository writes several globs as a single double-quoted
    /// scalar — <c>applyTo: "src/App/**,src/Modules/**"</c> — and the shared
    /// reader splits only the bracketed list form, correctly: a comma in a
    /// <c>meta</c> field's scalar is punctuation. It is a separator only in this
    /// one field, so that is where it is read.</para>
    /// </summary>
    private static IReadOnlyList<string> Globs(IReadOnlyList<string> values) =>
    [
        .. values
            .SelectMany(value => value.Split(','))
            .Select(glob => glob.Trim())
            .Where(glob => glob.Length > 0)
    ];

    /// <summary>
    /// The keys with no field of their own, in the order the block wrote them.
    ///
    /// <para>The order is read off the block's own lines, and only the order is:
    /// every value still comes from the one shared reader. That reader hands back
    /// a dictionary, and a dictionary is in insertion order only until something
    /// is removed from it — which it does do, for a field written as <c>null</c>
    /// or <c>[]</c>. Widening its contract to promise an order would be a change
    /// to what the <c>meta</c> fence depends on; reading the key names again here
    /// is not.</para>
    ///
    /// <para>A key that stated no value is not a row. <c>mode:</c> with nothing
    /// after it, <c>null</c> and <c>[]</c> all mean "not stated" — the same
    /// reading the fence gives them — and a label with nothing beside it tells a
    /// reader no more than the absence already does.</para>
    /// </summary>
    private static IReadOnlyList<MarkdownFrontmatterField> OtherFields(
        string block,
        IReadOnlyDictionary<string, IReadOnlyList<string>> fields)
    {
        var other = new List<MarkdownFrontmatterField>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in block.Split('\n'))
        {
            var trimmed = line.Trim();

            // A list item belongs to the key above it, which has already taken it.
            if (trimmed.Length == 0 || trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed == "-") continue;

            var separator = trimmed.IndexOf(':');
            if (separator <= 0) continue;

            // Kept as the file spelled it, for the label; looked up lower-cased,
            // which is the only spelling the reader answers to.
            var key = trimmed[..separator].Trim();
            var lookup = key.ToLowerInvariant();

            if (Named.Contains(lookup) || !seen.Add(lookup)) continue;
            if (!fields.TryGetValue(lookup, out var values) || values.Count == 0) continue;

            other.Add(new MarkdownFrontmatterField(key, values));
        }

        return other;
    }
}

/// <summary>
/// One frontmatter key a viewer has no particular field for, and what it said.
/// </summary>
/// <param name="Key">The key as the file spelled it.</param>
/// <param name="Values">Its values — one for a scalar, several for a list.</param>
public sealed record MarkdownFrontmatterField(string Key, IReadOnlyList<string> Values)
{
    /// <summary>
    /// The key as a label, reading the way the labels beside it do: <c>name</c>
    /// becomes "Name", <c>agent-name</c> becomes "Agent name".
    ///
    /// <para>No friendlier wording is invented for a key nobody here recognises.
    /// A label that renamed <c>mode</c> to something a reader could not find in
    /// the file would be a machine key translated into a guess.</para>
    /// </summary>
    public string Label
    {
        get
        {
            var words = Key.Replace('-', ' ').Replace('_', ' ').Trim();
            return words.Length == 0 ? Key : char.ToUpperInvariant(words[0]) + words[1..];
        }
    }

    /// <summary>The values as one line of text. A list of two is read as two
    /// things, so it is punctuated as two rather than run together.</summary>
    public string Text => string.Join(", ", Values);
}
