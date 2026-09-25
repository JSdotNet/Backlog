using Backlog.UI.Components.Devbook;

namespace Backlog.UI.Components.Metadata;

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
public static class MetadataReader
{
    /// <summary>The fence language that marks a metadata block.</summary>
    public const string FenceLanguage = "meta";

    private static readonly string[] ReferenceFields = ["related", "depends-on", "implements"];

    /// <summary>
    /// Every key this reader recognises — which is to say every key that does
    /// <em>not</em> end up in <see cref="MetadataRecord.Extra"/>.
    ///
    /// <para><c>order</c> is in the list and modelled nowhere. It is not metadata
    /// about a chapter: it is a directory listing a root document happens to keep
    /// in the same fence, and the schema no longer carries it. Leaving it out of
    /// this set would not remove it — it would move it into <c>Extra</c> and draw
    /// it as an unrecognised field, which states the field more loudly than
    /// modelling it ever did. So it is recognised and dropped.</para>
    ///
    /// <para>The nine decision and review state fields are in it too, and are
    /// modelled apart — see <see cref="MetadataRecord.State"/>. <c>ext.*</c> keys
    /// are not in it and do not need to be: they are recognised by their prefix and
    /// read on a pass of their own, because they keep the author's casing and an
    /// empty value that every field here loses.</para>
    /// </summary>
    private static readonly HashSet<string> KnownFields = new(
        [
            "status", "related", "depends-on", "implements", "issue",
            "order", "aliases", "alternatives", "type", "kind", "version",
            "effort", "roadmap", "feature-flag", "tests", "number", "index",
            "date", "deployment",
            .. DevbookSchema.StateFields
        ],
        StringComparer.Ordinal);

    /// <summary>Whether a fence opened a metadata block.</summary>
    public static bool IsMetaBlock(string? language) =>
        language is not null && language.Trim().Equals(FenceLanguage, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The seam a renderer uses: hand it the fence language and the body, and a
    /// block that is not metadata comes back empty rather than half-parsed.
    /// </summary>
    public static MetadataRecord ParseFence(string? fenceLanguage, string? body) =>
        IsMetaBlock(fenceLanguage) ? Parse(body) : MetadataRecord.Empty;

    /// <summary>
    /// Parses the text <em>inside</em> the fence — the caller has already stripped
    /// the fence lines themselves.
    /// </summary>
    public static MetadataRecord Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return MetadataRecord.Empty;

        var fields = ReadFields(body);
        var ext = ReadExtensions(body);
        if (fields.Count == 0 && ext.Count == 0) return MetadataRecord.Empty;

        var extra = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var (key, values) in fields)
        {
            if (KnownFields.Contains(key) || DevbookSchema.IsExtensionKey(key)) continue;
            extra[key] = values;
        }

        var references = new Dictionary<string, IReadOnlyList<DevbookReference>>(StringComparer.Ordinal);
        foreach (var field in ReferenceFields)
        {
            if (!fields.TryGetValue(field, out var values)) continue;

            var parsed = new List<DevbookReference>();
            var rejected = new List<string>();
            foreach (var value in values)
            {
                if (DevbookReference.TryParse(value, out var reference) && reference is not null)
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

        // `type` is the field; `kind` is how `tech/` used to spell it, and the
        // convention still parses the old name so a repository is not broken by a
        // sync. Read here in that order, and marked, so the report can say which.
        var type = Scalar(fields, "type");
        var kind = Scalar(fields, DevbookSchema.LegacyTechTypeField);

        return new MetadataRecord
        {
            Status = Scalar(fields, "status"),
            Type = type ?? kind,
            TypeReadFromKind = type is null && kind is not null,
            Related = references.GetValueOrDefault("related", []),
            DependsOn = references.GetValueOrDefault("depends-on", []),
            Implements = references.GetValueOrDefault("implements", []),
            Issue = Scalar(fields, "issue"),
            Aliases = fields.GetValueOrDefault("aliases", []),
            Alternatives = fields.GetValueOrDefault("alternatives", []),
            Kind = kind,
            Version = Scalar(fields, "version"),
            Effort = ParseEffort(Scalar(fields, "effort")),
            Roadmap = fields.GetValueOrDefault("roadmap", []),
            FeatureFlag = fields.GetValueOrDefault("feature-flag", []),
            Tests = fields.GetValueOrDefault("tests", []),
            Number = ParseNonNegative(Scalar(fields, "number")),
            Index = Scalar(fields, "index"),
            Date = Scalar(fields, "date"),
            Deployment = Scalar(fields, "deployment"),
            Ext = ext,
            State = new MetadataState
            {
                ApprovedBy = Scalar(fields, "approved-by"),
                ApprovedAt = Scalar(fields, "approved-at"),
                ApprovedHash = Scalar(fields, "approved-hash"),
                AcceptedBy = Scalar(fields, "accepted-by"),
                AcceptedAt = Scalar(fields, "accepted-at"),
                AcceptedHash = Scalar(fields, "accepted-hash"),
                Review = Scalar(fields, "review"),
                Reviewer = Scalar(fields, "reviewer"),
                ReviewAt = Scalar(fields, "review-at")
            },
            Extra = extra
        };
    }

    /// <summary>
    /// Every <c>ext.&lt;plugin&gt;.&lt;key&gt;</c> line, keyed by what follows
    /// <c>ext.</c> and valued by what follows the first colon — both exactly as
    /// the author wrote them.
    ///
    /// <para>A pass of its own rather than a lookup in <see cref="ReadFields"/>,
    /// because that reader normalises the two things an extension key must keep. It
    /// lower-cases keys, and an extension's key belongs to its plugin, which may
    /// spell it however it likes; and it drops a key with no value, which is the
    /// omit-when-empty rule the convention explicitly exempts <c>ext</c> from. So an
    /// empty value comes back as an empty string, and a value is never unquoted,
    /// split into a list, or read as <c>null</c> — the reader has no opinion on what
    /// it means, and neither does anything downstream of it.</para>
    ///
    /// <para>The same key written twice keeps the last value, which is what the
    /// field reader does with any other key.</para>
    /// </summary>
    private static Dictionary<string, string> ReadExtensions(string body)
    {
        var ext = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var raw in body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || IsItem(line)) continue;

            var separator = line.IndexOf(':');
            if (separator <= 0) continue;

            var key = line[..separator].Trim();
            if (!DevbookSchema.IsExtensionKey(key)) continue;

            var name = key[DevbookSchema.ExtensionPrefix.Length..];
            if (name.Length == 0) continue;

            ext[name] = line[(separator + 1)..].Trim();
        }

        return ext;
    }

    // Story points are an integer the UI wants to show and compare, and this
    // side is a reader: a value it cannot read back as a non-negative integer is
    // treated as "no effort" rather than allowed to throw. The raw text is not
    // kept — the number is the whole value, and an unparseable one carries
    // nothing a viewer could honestly display.
    //
    // `number` is read the same way and for the same reason: a document's place
    // in its directory is a count, and one that does not parse is no place.
    private static int? ParseEffort(string? value) => ParseNonNegative(value);

    private static int? ParseNonNegative(string? value) =>
        int.TryParse(value, out var number) && number >= 0 ? number : null;

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
