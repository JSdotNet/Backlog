using System.Text;
using System.Text.RegularExpressions;

namespace Backlog.UI.Components.Devbook;

/// <summary>
/// One chapter of a <c>.domain</c> file as the Domain view reads it (local ADR
/// 0036): its heading, the fields of its <c>meta</c> block it needs, the prose it
/// holds itself, and the chapters beneath it.
/// </summary>
/// <param name="Title">The heading as written.</param>
/// <param name="Anchor">The heading's slug, GitHub's rule: what a reference to
/// this chapter names after the <c>#</c>.</param>
/// <param name="Level">The heading level, 2 for a <c>##</c>.</param>
/// <param name="Type">The block's <c>type</c>, or <see langword="null"/> when it
/// states none.</param>
/// <param name="Related">The block's <c>related</c> entries as written.</param>
/// <param name="Aliases">The block's <c>aliases</c> as written.</param>
/// <param name="Prose">The chapter's own markdown: its body without its heading,
/// its <c>meta</c> block or any <c>annotation</c> fence, and with every untyped
/// sub-chapter kept in place — a sub-heading that states no <c>type</c> is part of
/// the prose rather than a unit of its own.</param>
/// <param name="Children">The typed chapters one level down, and the untyped ones
/// too, so an event's <c>### Consumers</c> is reachable.</param>
public sealed record DomainChapter(
    string Title,
    string Anchor,
    int Level,
    string? Type,
    IReadOnlyList<string> Related,
    IReadOnlyList<string> Aliases,
    string Prose,
    IReadOnlyList<DomainChapter> Children)
{
    /// <summary>The chapter's first sentence as plain text — markdown links read
    /// as their words, code spans as their text. What ADR 0036 §5 makes a unit's
    /// summary.</summary>
    public string Summary => DomainUnits.FirstSentence(Prose);

    /// <summary>The block's <c>tests</c> entries as written: the tests that prove
    /// the rule a chapter states, which the Always true tab marks it by.</summary>
    public IReadOnlyList<string> Tests { get; init; } = [];

    /// <summary>Every chapter under this one, depth first.</summary>
    public IEnumerable<DomainChapter> Descendants() =>
        Children.SelectMany(child => child.Descendants().Prepend(child));
}

/// <summary>One <c>.domain</c> Markdown file read into chapters.</summary>
/// <param name="Path">The file's path as the host spells it —
/// <c>.devbook/domain/tasks/domain.md</c> or <c>.domain/tasks/domain.md</c>. A
/// <c>related</c> entry is matched against it whichever spelling either side
/// uses.</param>
/// <param name="Title">The <c># </c> heading.</param>
/// <param name="Chapters">The <c>##</c> chapters, in file order.</param>
public sealed record DomainChapterFile(string Path, string Title, IReadOnlyList<DomainChapter> Chapters)
{
    /// <summary>Every chapter in the file, depth first.</summary>
    public IEnumerable<DomainChapter> AllChapters() =>
        Chapters.SelectMany(chapter => chapter.Descendants().Prepend(chapter));

    /// <summary>The reference that names <paramref name="chapter"/>: this file's
    /// path and the chapter's anchor.</summary>
    public string ReferenceTo(DomainChapter chapter) => $"{Path}#{chapter.Anchor}";
}

/// <summary>What a pickable unit of a domain file is.</summary>
public enum DomainUnitKind
{
    /// <summary>An <c>aggregate</c>: About and Always true.</summary>
    Aggregate,

    /// <summary>A <c>domain-service</c>: one page.</summary>
    DomainService,

    /// <summary>The <c>ubiquitous-language</c> grouping, read as Glossary.</summary>
    Glossary,

    /// <summary>A <c>shared-value-objects</c> or <c>shared-enums</c> grouping that
    /// has rules: its Always true, on the base page.</summary>
    Shared
}

/// <summary>A unit the Domain page offers: an aggregate, a domain service, or
/// the Glossary.</summary>
/// <param name="Key">The chapter's anchor, unique within its file.</param>
/// <param name="Label">What the chapter bar calls it: the heading as written, and
/// Glossary for the ubiquitous-language grouping.</param>
/// <param name="Type">The <c>type</c> the type marker draws.</param>
/// <param name="Reference">The path and anchor that name the chapter.</param>
public sealed record DomainUnit(DomainUnitKind Kind, string Key, string Label, string Type, string Reference, DomainChapter Chapter);

/// <summary>An entity or value object an aggregate owns, as its card reads.</summary>
/// <param name="ModelName">The name the model gives it — its first alias that is
/// an identifier, else its heading run together.</param>
public sealed record DomainMember(string Title, string Type, string Summary, string ModelName);

/// <summary>An enum an aggregate owns, and the values it names.</summary>
/// <param name="Summary">Its first sentence, shown when no value could be read
/// out of it.</param>
public sealed record DomainEnum(string Title, IReadOnlyList<string> Values, string Summary);

/// <summary>A domain event a unit raises, as one line: what it is and who listens.
/// Never its payload, which is Model's (ADR 0036 §6).</summary>
public sealed record DomainEventLine(string Name, string Summary, IReadOnlyList<string> Consumers);

/// <summary>A requirement a domain service is held to, as its "You can count on"
/// line reads.</summary>
/// <param name="Reference">The requirement chapter's path and anchor, which the
/// line opens on the Requirements page.</param>
public sealed record DomainGuarantee(string Title, string Reference);

/// <summary>A term of the Glossary.</summary>
public sealed record DomainTerm(string Title, string Summary);

/// <summary>
/// Reads the units of a <c>.domain</c> file for the Domain view (local ADR 0036
/// §4–6). Pure: text in, records out, so the storybook draws the same pages from a
/// sample string that the app draws from a repository.
/// <para>
/// Everything is read from what the chapters already say, as §5 decides: headings
/// as written, first sentences as summaries, <c>related</c> as the edges. No field
/// is asked of a chapter for the view.
/// </para>
/// </summary>
public static partial class DomainUnits
{
    private static readonly Regex Heading = HeadingPattern();

    /// <summary>Reads <paramref name="markdown"/>, the content of the file at
    /// <paramref name="path"/>, into chapters.</summary>
    public static DomainChapterFile Parse(string markdown, string path)
    {
        var lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var headings = new List<(int Line, int Level, string Text)>();
        string? fence = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (fence is null && (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)))
            {
                fence = trimmed[..3];
                continue;
            }
            if (fence is not null)
            {
                if (trimmed.StartsWith(fence, StringComparison.Ordinal)) fence = null;
                continue;
            }

            var match = Heading.Match(lines[i]);
            if (match.Success) headings.Add((i, match.Groups[1].Value.Length, match.Groups[2].Value.Trim()));
        }

        var title = headings.FirstOrDefault(h => h.Level == 1).Text ?? string.Empty;
        var chapters = Build(lines, headings, 0, headings.Count, 2);
        return new DomainChapterFile(path, title, chapters);
    }

    /// <summary>The units a Domain page offers, in file order: every
    /// <c>aggregate</c> and <c>domain-service</c> chapter at any depth, and the
    /// <c>ubiquitous-language</c> grouping as Glossary.</summary>
    public static IReadOnlyList<DomainUnit> Of(DomainChapterFile file)
    {
        var units = new List<DomainUnit>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chapter in file.AllChapters())
        {
            if (KindOf(chapter.Type) is not { } kind) continue;

            // Two chapters of one heading share an anchor; the bar keys each once.
            var key = chapter.Anchor;
            for (var n = 1; !keys.Add(key); n++) key = $"{chapter.Anchor}-{n}";

            units.Add(new DomainUnit(kind, key, kind == DomainUnitKind.Glossary ? "Glossary" : chapter.Title, chapter.Type!, file.ReferenceTo(chapter), chapter));
        }
        return units;
    }

    /// <summary>The entities and value objects <paramref name="aggregate"/> owns:
    /// its sub-chapters typed <c>entity</c> or <c>value-object</c>.</summary>
    public static IReadOnlyList<DomainMember> Members(DomainChapter aggregate) =>
    [
        .. Owned(aggregate)
            .Where(child => child.Type is "entity" or "value-object")
            .Select(child => new DomainMember(child.Title, child.Type!, child.Summary, ModelName(child)))
    ];

    /// <summary>The enums <paramref name="aggregate"/> owns, each with the values
    /// its first sentence names in code spans.</summary>
    public static IReadOnlyList<DomainEnum> Enums(DomainChapter aggregate) =>
    [
        .. Owned(aggregate)
            .Where(child => child.Type == "enum")
            .Select(child => new DomainEnum(child.Title, EnumValues(child.Prose), child.Summary))
    ];

    /// <summary>The typed chapters an aggregate owns: its direct sub-chapters, and
    /// those under an untyped sub-heading that only groups them.</summary>
    private static IEnumerable<DomainChapter> Owned(DomainChapter aggregate) =>
        aggregate.Children.SelectMany(child => child.Type is null ? Owned(child) : [child]);

    /// <summary>The terms of a Glossary, each with its first sentence.</summary>
    public static IReadOnlyList<DomainTerm> Terms(DomainChapter glossary) =>
    [
        .. glossary.Children
            .Where(child => child.Type is null or "term")
            .Select(child => new DomainTerm(child.Title, child.Summary))
    ];

    /// <summary>
    /// The events the unit <paramref name="unitReference"/> names raises: the
    /// <c>domain-event</c> chapters, in <paramref name="files"/>, whose
    /// <c>related</c> names it. Each is its name, its first sentence and the names
    /// of its consumers — the lines of its <c>### Consumers</c> up to the first
    /// comma or dash, which is where the convention's "X, which …" turns from who
    /// to why.
    /// </summary>
    public static IReadOnlyList<DomainEventLine> EventsRaisedBy(string unitReference, IEnumerable<DomainChapterFile> files)
    {
        var target = Normalize(unitReference);
        return
        [
            .. files.SelectMany(file => file.AllChapters()
                .Where(chapter => chapter.Type == "domain-event")
                .Where(chapter => chapter.Related.Any(related => Resolve(related, file.Path) == target))
                .Select(chapter => new DomainEventLine(chapter.Title, chapter.Summary, Consumers(chapter))))
        ];
    }

    /// <summary>
    /// The requirements a domain service is held to: every requirement chapter in
    /// <paramref name="requirementFiles"/> whose <c>related</c> — or whose
    /// requirements group's — names the service, and every requirement the
    /// service's own <c>related</c> names, a group standing for each requirement in
    /// it. In file order, each once.
    /// </summary>
    public static IReadOnlyList<DomainGuarantee> Guarantees(DomainUnit service, IEnumerable<DomainChapterFile> requirementFiles)
    {
        var self = Normalize(service.Reference);
        var named = service.Chapter.Related
            .Select(related => Resolve(related, PathOf(service.Reference)))
            .ToHashSet(StringComparer.Ordinal);

        var result = new List<DomainGuarantee>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in requirementFiles)
        {
            foreach (var group in file.Chapters)
            {
                Walk(file, group, inherited: false);
            }
        }
        return result;

        void Walk(DomainChapterFile file, DomainChapter chapter, bool inherited)
        {
            var reference = file.ReferenceTo(chapter);
            var key = Normalize(reference);
            var chosen = inherited
                || named.Contains(key)
                || chapter.Related.Any(related => Resolve(related, file.Path) == self);

            if (IsRequirement(chapter))
            {
                if (chosen && seen.Add(key)) result.Add(new DomainGuarantee(RequirementTitle(chapter.Title), reference));
                return;
            }

            foreach (var child in chapter.Children) Walk(file, child, chosen);
        }
    }

    /// <summary>A unit's first sentence as plain text, or the empty string.</summary>
    public static string FirstSentence(string? markdown)
    {
        var paragraph = FirstParagraph(markdown);
        if (paragraph.Length == 0) return string.Empty;
        return Plain(SentenceOf(paragraph));
    }

    /// <summary>Markdown inline syntax reduced to its words: a link to its text,
    /// a code span to its content, emphasis to nothing.</summary>
    public static string Plain(string markdown)
    {
        var text = MarkdownLink().Replace(markdown, "$1");
        text = text.Replace("`", string.Empty).Replace("**", string.Empty).Replace("__", string.Empty);
        text = Emphasis().Replace(text, "$1");
        return Whitespace().Replace(text, " ").Trim();
    }

    /// <summary>GitHub's anchor for a heading: lower case, everything but letters,
    /// digits, underscores, spaces and hyphens dropped, each space a hyphen.</summary>
    public static string Slug(string heading) =>
        NotSlug().Replace(heading.Trim().ToLowerInvariant(), string.Empty).Replace(' ', '-');

    private static IReadOnlyList<DomainChapter> Build(string[] lines, List<(int Line, int Level, string Text)> headings, int from, int to, int level)
    {
        var chapters = new List<DomainChapter>();
        for (var h = from; h < to; h++)
        {
            // A heading deeper than this level before the first one at it — a `###`
            // straight under the title — belongs to no chapter and is skipped.
            if (headings[h].Level != level) continue;

            var end = h + 1;
            while (end < to && headings[end].Level > level) end++;
            var stop = end < headings.Count ? headings[end].Line : lines.Length;
            chapters.Add(Chapter(lines, headings, h, end, stop));
            h = end - 1;
        }
        return chapters;
    }

    private static DomainChapter Chapter(string[] lines, List<(int Line, int Level, string Text)> headings, int h, int end, int stop)
    {
        var (line, level, text) = headings[h];
        var bodyStart = line + 1;
        var meta = ReadMeta(lines, ref bodyStart);

        // The chapters one level down, wherever they sit — a `####` directly under
        // a `##` counts as one level down too, so nothing is dropped.
        var childLevel = Enumerable.Range(h + 1, end - h - 1).Select(i => headings[i].Level).DefaultIfEmpty(level + 1).Min();
        var children = Build(lines, headings, h + 1, end, childLevel);

        // The prose is the body up to the first typed sub-chapter's heading, with
        // each untyped one kept in place: a sub-heading with no type is still prose.
        var prose = new StringBuilder();
        var firstChild = h + 1 < end ? headings[h + 1].Line : stop;
        Append(prose, lines, bodyStart, firstChild);
        var c = 0;
        for (var i = h + 1; i < end; i++)
        {
            if (headings[i].Level != childLevel) continue;
            var child = children[c++];
            var childEnd = i + 1;
            while (childEnd < end && headings[childEnd].Level > childLevel) childEnd++;
            if (child.Type is not null) continue;
            var childStop = childEnd < headings.Count ? headings[childEnd].Line : stop;
            Append(prose, lines, headings[i].Line, Math.Min(childStop, stop));
        }

        return new DomainChapter(
            text,
            Slug(text),
            level,
            meta.GetValueOrDefault("type") is { Length: > 0 } type ? type : null,
            List(meta.GetValueOrDefault("related")),
            List(meta.GetValueOrDefault("aliases")),
            WithoutFences(prose.ToString(), "annotation", "meta").Trim(),
            children)
        {
            Tests = List(meta.GetValueOrDefault("tests"))
        };
    }

    private static void Append(StringBuilder builder, string[] lines, int from, int to)
    {
        for (var i = from; i < to && i < lines.Length; i++) builder.Append(lines[i]).Append('\n');
    }

    private static Dictionary<string, string> ReadMeta(string[] lines, ref int index)
    {
        var at = index;
        while (at < lines.Length && string.IsNullOrWhiteSpace(lines[at])) at++;
        if (at >= lines.Length || !string.Equals(lines[at].Trim(), "```meta", StringComparison.OrdinalIgnoreCase)) return [];

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        at++;
        while (at < lines.Length && !lines[at].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            var colon = lines[at].IndexOf(':');
            if (colon > 0) result[lines[at][..colon].Trim()] = lines[at][(colon + 1)..].Trim();
            at++;
        }
        index = Math.Min(at + 1, lines.Length);
        return result;
    }

    private static IReadOnlyList<string> List(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var text = value.Trim();
        if (text.StartsWith('[') && text.EndsWith(']')) text = text[1..^1];
        return
        [
            .. text.Split(',')
                .Select(entry => entry.Trim().Trim('"', '\''))
                .Where(entry => entry.Length > 0)
        ];
    }

    private static string WithoutFences(string markdown, params string[] languages)
    {
        var builder = new StringBuilder();
        var skipping = false;
        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!skipping && trimmed.StartsWith("```", StringComparison.Ordinal)
                && languages.Any(language => string.Equals(trimmed[3..].Trim(), language, StringComparison.OrdinalIgnoreCase)))
            {
                skipping = true;
                continue;
            }
            if (skipping)
            {
                if (trimmed.StartsWith("```", StringComparison.Ordinal)) skipping = false;
                continue;
            }
            builder.Append(line).Append('\n');
        }
        return builder.ToString();
    }

    private static string FirstParagraph(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        var paragraph = new List<string>();
        var inFence = false;
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                if (paragraph.Count > 0) break;
                continue;
            }
            if (inFence) continue;
            if (line.Length == 0)
            {
                if (paragraph.Count > 0) break;
                continue;
            }
            if (paragraph.Count == 0 && (line.StartsWith('#') || line.StartsWith('|') || line.StartsWith('>') || line.StartsWith("<!--", StringComparison.Ordinal))) continue;
            paragraph.Add(ListMarker().Replace(line, string.Empty));
        }
        return string.Join(' ', paragraph);
    }

    private static string SentenceOf(string paragraph)
    {
        // A full stop ends the sentence only outside a code span and a link, and
        // only where a space or the end follows it — `e.g.` mid-sentence aside.
        var inCode = false;
        var bracket = 0;
        for (var i = 0; i < paragraph.Length; i++)
        {
            var ch = paragraph[i];
            if (ch == '`') inCode = !inCode;
            else if (!inCode && ch is '[' or '(') bracket++;
            else if (!inCode && ch is ']' or ')') bracket = Math.Max(0, bracket - 1);
            else if (!inCode && bracket == 0 && ch is '.' or '!' or '?' && (i + 1 == paragraph.Length || paragraph[i + 1] == ' ')
                && !EndsWithAbbreviation(paragraph, i))
            {
                return paragraph[..(i + 1)];
            }
        }
        return paragraph;
    }

    /// <summary>Whether the full stop at <paramref name="at"/> closes an
    /// abbreviation — <c>e.g.</c>, <c>i.e.</c> — rather than the sentence.</summary>
    private static bool EndsWithAbbreviation(string paragraph, int at) =>
        at >= 3 && paragraph[(at - 3)..(at + 1)] is "e.g." or "i.e." or "E.g." or "I.e.";

    private static IReadOnlyList<string> EnumValues(string prose)
    {
        var sentence = SentenceOf(FirstParagraph(prose));
        // A range — `monday` through `sunday` — names its ends, not its values;
        // the row then reads the sentence instead.
        if (ValueRange().IsMatch(sentence)) return [];

        IEnumerable<string> values = CodeSpan().Matches(sentence).Select(match => match.Groups[1].Value);

        // An enum written as a sentence ending in a colon and a list of its values
        // names each value as the code span its list item opens with.
        if (!values.Any())
        {
            values = prose.Split('\n')
                .Select(line => LeadingValue().Match(line))
                .Where(match => match.Success)
                .Select(match => match.Groups[1].Value);
        }

        return [.. values.Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal)];
    }

    private static IReadOnlyList<string> Consumers(DomainChapter domainEvent)
    {
        var consumers = domainEvent.Children.FirstOrDefault(child => string.Equals(child.Title, "Consumers", StringComparison.OrdinalIgnoreCase));
        if (consumers is null) return [];

        return
        [
            .. consumers.Prose.Split('\n')
                .Select(line => line.TrimEnd())
                .Where(line => line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
                .Select(line => ConsumerName(Plain(line[2..])))
                .Where(name => name.Length > 0)
        ];
    }

    private static string ConsumerName(string item)
    {
        var cut = item.Length;
        foreach (var separator in new[] { ", ", " — ", " - ", " – ", ": " })
        {
            var at = item.IndexOf(separator, StringComparison.Ordinal);
            if (at > 0 && at < cut) cut = at;
        }
        return item[..cut].Trim().TrimEnd('.', '—', '-', '–').Trim();
    }

    /// <summary>The heading run together, when an alias spells it that way in any
    /// case — <c>Recurrence</c> over an earlier <c>Repeat</c> — else the first alias
    /// that is an identifier, else the heading run together.</summary>
    private static string ModelName(DomainChapter chapter)
    {
        var runTogether = string.Concat(Words().Matches(chapter.Title).Select(word => char.ToUpperInvariant(word.Value[0]) + word.Value[1..]));
        return chapter.Aliases.Any(alias => string.Equals(alias, runTogether, StringComparison.OrdinalIgnoreCase))
            ? runTogether
            : chapter.Aliases.FirstOrDefault(alias => Identifier().IsMatch(alias)) ?? runTogether;
    }

    private static bool IsRequirement(DomainChapter chapter) =>
        chapter.Type == "requirement"
        || (chapter.Type is null && chapter.Title.StartsWith("Requirement:", StringComparison.OrdinalIgnoreCase));

    private static string RequirementTitle(string title) =>
        title.StartsWith("Requirement:", StringComparison.OrdinalIgnoreCase) ? title["Requirement:".Length..].Trim() : title;

    private static DomainUnitKind? KindOf(string? type) => type switch
    {
        "aggregate" => DomainUnitKind.Aggregate,
        "domain-service" => DomainUnitKind.DomainService,
        "ubiquitous-language" => DomainUnitKind.Glossary,
        _ => null
    };

    private static string PathOf(string reference)
    {
        var hash = reference.IndexOf('#');
        return hash < 0 ? reference : reference[..hash];
    }

    /// <summary>A reference written in <paramref name="fromDocument"/>, rooted
    /// and spelled the one way two references are compared in: beneath the domain
    /// folder, lower case — <c>tasks/domain.md#task</c>.</summary>
    internal static string Resolve(string related, string fromDocument)
    {
        var text = related.Trim().Trim('"', '\'');
        if (text.StartsWith('#')) return Normalize(PathOf(fromDocument) + text);
        if (IsRooted(text)) return Normalize(text);

        // Relative, the way a Markdown viewer reads it: against the document's folder.
        var folder = Normalize(PathOf(fromDocument));
        var slash = folder.LastIndexOf('/');
        var parts = (slash < 0 ? [] : folder[..slash].Split('/')).ToList();
        foreach (var segment in text.Split('/'))
        {
            if (segment is "." or "") continue;
            if (segment == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(segment);
        }
        return string.Join('/', parts).ToLowerInvariant();
    }

    /// <summary>A rooted reference beneath the domain folder, lower case.</summary>
    internal static string Normalize(string reference)
    {
        var text = reference.Trim().Replace('\\', '/');
        if (text.StartsWith("./", StringComparison.Ordinal)) text = text[2..];
        text = text.TrimStart('/');
        foreach (var prefix in new[] { ".devbook/domain/", ".domain/" })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return text[prefix.Length..].ToLowerInvariant();
        }
        return text.ToLowerInvariant();
    }

    private static bool IsRooted(string text)
    {
        var trimmed = text.TrimStart('.', '/');
        return text.StartsWith(".devbook/", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith(".domain/", StringComparison.OrdinalIgnoreCase)
            || (text.StartsWith('/') && trimmed.Length > 0);
    }

    [GeneratedRegex(@"^(#{1,6})[ \t]+(.+?)(?:[ \t]+#+)?[ \t]*$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"(?<![\w*])\*([^*]+)\*(?![\w*])")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[^\p{L}\p{N}_\- ]")]
    private static partial Regex NotSlug();

    [GeneratedRegex(@"^\s*(?:[-*+]|\d+[.)])\s+")]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"^\s*[-*+]\s+`([^`]+)`")]
    private static partial Regex LeadingValue();

    [GeneratedRegex(@"`[^`]+`\s+(?:through|to|until)\s+`[^`]+`")]
    private static partial Regex ValueRange();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Words();
}
