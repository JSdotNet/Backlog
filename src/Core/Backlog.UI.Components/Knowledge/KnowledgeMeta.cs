namespace Backlog.UI.Components.Knowledge;

/// <summary>
/// Reads the fenced <c>meta</c> block a knowledge chapter or file carries under
/// its heading.
///
/// <para>Hand-rolled rather than handed to a YAML library, and deliberately so:
/// this component library carries no dependency beyond the Blazor packages, and
/// the block is a flat list of scalars and one-level lists — the same shape the
/// application's own knowledge readers already parse by hand.</para>
///
/// <para>It is forgiving on purpose. The convention says to omit an empty field,
/// but this repository's own <c>.arc42</c> template writes <c>related: []</c> and
/// <c>issue: null</c>, and inline lists appear both quoted and bare. All of those
/// read back the same way here; what a file says is not something a viewer gets
/// to refuse.</para>
/// </summary>
public static class KnowledgeMeta
{
    /// <summary>The fence language that marks a metadata block.</summary>
    public const string FenceLanguage = "meta";

    private static readonly string[] ReferenceFields = ["related", "depends-on", "implements"];

    private static readonly HashSet<string> KnownFields = new(StringComparer.Ordinal)
    {
        "status", "related", "depends-on", "implements", "issue",
        "order", "aliases", "alternatives", "kind", "version",
        "effort", "roadmap"
    };

    /// <summary>Whether a fence opened a metadata block.</summary>
    public static bool IsMetaBlock(string? language) =>
        language is not null && language.Trim().Equals(FenceLanguage, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The seam a renderer uses: hand it the fence language and the body, and a
    /// block that is not metadata comes back empty rather than half-parsed.
    /// </summary>
    public static KnowledgeMetadata ParseFence(string? fenceLanguage, string? body) =>
        IsMetaBlock(fenceLanguage) ? Parse(body) : KnowledgeMetadata.Empty;

    /// <summary>
    /// Parses the text <em>inside</em> the fence — the caller has already stripped
    /// the fence lines themselves.
    /// </summary>
    public static KnowledgeMetadata Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return KnowledgeMetadata.Empty;

        var fields = ReadFields(body);
        if (fields.Count == 0) return KnowledgeMetadata.Empty;

        var extra = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var (key, values) in fields)
        {
            if (KnownFields.Contains(key)) continue;
            extra[key] = values;
        }

        var references = new Dictionary<string, IReadOnlyList<KnowledgeReference>>(StringComparer.Ordinal);
        foreach (var field in ReferenceFields)
        {
            if (!fields.TryGetValue(field, out var values)) continue;

            var parsed = new List<KnowledgeReference>();
            var rejected = new List<string>();
            foreach (var value in values)
            {
                if (KnowledgeReference.TryParse(value, out var reference) && reference is not null)
                {
                    parsed.Add(reference);
                }
                else
                {
                    rejected.Add(value);
                }
            }

            references[field] = parsed;

            // An entry that is not addressable is still something the author
            // wrote. Dropping it would hide the typo that caused it.
            if (rejected.Count > 0) extra[field] = rejected;
        }

        return new KnowledgeMetadata
        {
            Status = Scalar(fields, "status"),
            Related = references.GetValueOrDefault("related", []),
            DependsOn = references.GetValueOrDefault("depends-on", []),
            Implements = references.GetValueOrDefault("implements", []),
            Issue = Scalar(fields, "issue"),
            Order = fields.GetValueOrDefault("order", []),
            Aliases = fields.GetValueOrDefault("aliases", []),
            Alternatives = fields.GetValueOrDefault("alternatives", []),
            Kind = Scalar(fields, "kind"),
            Version = Scalar(fields, "version"),
            Effort = ParseEffort(Scalar(fields, "effort")),
            Roadmap = fields.GetValueOrDefault("roadmap", []),
            Extra = extra
        };
    }

    // Story points are an integer the UI wants to show and compare, and this
    // side is a reader: a value it cannot read back as a non-negative integer is
    // treated as "no effort" rather than allowed to throw. The raw text is not
    // kept — the number is the whole value, and an unparseable one carries
    // nothing a viewer could honestly display.
    private static int? ParseEffort(string? value) =>
        int.TryParse(value, out var effort) && effort >= 0 ? effort : null;

    private static string? Scalar(IReadOnlyDictionary<string, IReadOnlyList<string>> fields, string key) =>
        fields.TryGetValue(key, out var values) && values.Count > 0 ? values[0] : null;

    /// <summary>
    /// Splits the block into keys and their values. Everything is a list here —
    /// a scalar is simply a list of one — so the field mapping above has one
    /// shape to read rather than two.
    ///
    /// <para>Internal rather than private because a markdown file's YAML
    /// frontmatter is written in exactly these shapes — <c>key: value</c>,
    /// <c>key: [a, b]</c>, and a key with a dash list under it — and
    /// <see cref="Markdown.MarkdownFrontmatter"/> reads it through here. A second
    /// reader for the same three shapes would be a second set of quoting and
    /// empty-value quirks to keep in step with the files.</para>
    ///
    /// <para>Keys come back lower-cased, so a caller looks up
    /// <c>applyto</c> and not <c>applyTo</c>.</para>
    /// </summary>
    internal static Dictionary<string, IReadOnlyList<string>> ReadFields(string body)
    {
        var lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var fields = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0) continue;

            // A list item reaching this loop belongs to no key — the key that
            // owned it would already have swallowed it below.
            if (IsItem(line)) continue;

            // The first colon only: `issue: https://…` and every chapter
            // reference carry colons and hashes of their own further along.
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;

            var key = line[..separator].Trim().ToLowerInvariant();
            if (key.Length == 0) continue;

            var value = line[(separator + 1)..].Trim();
            var values = value.Length == 0
                ? ReadBlockList(lines, ref index)
                : ReadInlineValue(value);

            if (values.Count == 0)
            {
                // `related: []`, `issue: null`, or a key with nothing under it.
                // All three mean the field was not stated.
                fields.Remove(key);
                continue;
            }

            fields[key] = values;
        }

        return fields;
    }

    /// <summary>The dash-prefixed lines under a key that gave no value on its own
    /// line. Indentation is not load-bearing: these blocks are one level deep.</summary>
    private static IReadOnlyList<string> ReadBlockList(string[] lines, ref int index)
    {
        var items = new List<string>();

        var lookahead = index + 1;
        while (lookahead < lines.Length)
        {
            var line = lines[lookahead].Trim();
            if (line.Length == 0)
            {
                lookahead++;
                continue;
            }

            if (!IsItem(line)) break;

            var item = Unquote(line[1..].Trim());
            if (item.Length > 0) items.Add(item);

            index = lookahead;
            lookahead++;
        }

        return items;
    }

    /// <summary>A value written on the key's own line: either an inline list or a
    /// single scalar.</summary>
    private static IReadOnlyList<string> ReadInlineValue(string value)
    {
        if (value.StartsWith('[') && value.EndsWith(']'))
        {
            var inner = value[1..^1];
            return [.. inner.Split(',')
                .Select(Unquote)
                .Where(item => item.Length > 0)];
        }

        var scalar = Unquote(value);

        // `null` is how the template spells "no value"; the convention would
        // rather the field were left out entirely, and here the two agree.
        return scalar.Length == 0 || scalar.Equals("null", StringComparison.OrdinalIgnoreCase)
            ? []
            : [scalar];
    }

    private static bool IsItem(string trimmedLine) =>
        trimmedLine.StartsWith("- ", StringComparison.Ordinal) || trimmedLine == "-";

    private static string Unquote(string value)
    {
        var text = value.Trim();
        if (text.Length >= 2
            && ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\'')))
        {
            text = text[1..^1].Trim();
        }

        return text;
    }
}
