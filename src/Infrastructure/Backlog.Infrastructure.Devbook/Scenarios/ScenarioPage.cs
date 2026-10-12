using System.Text.RegularExpressions;

namespace Backlog.Infrastructure.Devbook.Scenarios;

/// <summary>
/// One scenario page, read: a <c>type: scenario</c> file under <c>.devbook/domain/</c>
/// holding one end-to-end journey, its six setup fields, its <c>##</c> parts with their
/// steps, and the <c>shot:&lt;label&gt;</c> screenshot points between the step lists.
/// <para>
/// A port of devbook's <c>parse.mjs</c> (<c>.devbook/_tools/scenarios/</c>), line for
/// line where it decides anything the signature hashes: the signature is only worth
/// comparing because every implementation reads a page the same way, and
/// <c>scenario-page.vector.json</c> beside that file is what proves this one does.
/// </para>
/// </summary>
public sealed record ScenarioPage(
    string Path,
    string Stem,
    string? Title,
    bool IsScenario,
    IReadOnlyDictionary<string, ScenarioMetaValue> Meta,
    ScenarioSetup Setup,
    IReadOnlyList<ScenarioPart> Parts,
    IReadOnlyList<ScenarioShotPoint> Shots)
{
    /// <summary>The page's <c>status</c> — <c>draft</c> or <c>proposed</c> — or null:
    /// the journey is in force.</summary>
    public string? Status => Meta.TryGetValue("status", out var status) ? status.Scalar : null;

    /// <summary>Every label in page order, each once.</summary>
    public IReadOnlyList<string> Labels => [.. Shots.Select(shot => shot.Label).Distinct(StringComparer.Ordinal)];
}

/// <summary>The six fields that say where a journey starts and in which
/// configuration it runs. <see cref="Profile"/> null reads as <c>default</c>.</summary>
public sealed record ScenarioSetup(
    string? Start,
    IReadOnlyList<string> Actor,
    IReadOnlyList<string> Data,
    string? Profile,
    IReadOnlyList<string> Flags,
    IReadOnlyList<string> Settings)
{
    public static ScenarioSetup Empty { get; } = new(null, [], [], null, [], []);

    public bool IsEmpty => Start is null && Actor.Count == 0 && Data.Count == 0 && Profile is null && Flags.Count == 0 && Settings.Count == 0;
}

/// <summary>One <c>##</c> part: a title the actor reaches, its anchor, its steps and
/// its screenshot points.</summary>
public sealed record ScenarioPart(string Title, string Anchor, int Line, IReadOnlyList<ScenarioStep> Steps, IReadOnlyList<ScenarioShotPoint> Shots);

/// <summary>One step. <see cref="Keyword"/> is null for a list item that opens with
/// none of <c>Given</c>, <c>When</c>, <c>Then</c>, <c>And</c>.</summary>
public sealed record ScenarioStep(string? Keyword, string Text, int Line);

/// <summary>A <c>![caption](shot:label)</c> on the page. <see cref="Part"/> is the
/// anchor of the part it sits in, null above the first part.</summary>
public sealed record ScenarioShotPoint(string Label, string Caption, int Line, string? Part, bool InStepList);

/// <summary>A <c>meta</c> value: a scalar, or a <c>[a, b]</c> list.</summary>
public sealed record ScenarioMetaValue(string? Scalar, IReadOnlyList<string>? List)
{
    public IReadOnlyList<string> AsList() =>
        List ?? (string.IsNullOrEmpty(Scalar) ? [] : [Scalar]);
}

/// <summary>The parser. See <see cref="ScenarioPage"/>.</summary>
public static partial class ScenarioPageParser
{
    /// <summary>Where a repository keeps its runs, profiles and data sets.</summary>
    public const string ScenarioFolder = ".devbook/scenarios";

    /// <summary>The classic layout's scenario folder, which spec-manager's
    /// <c>.test</c> tooling writes version 1 runs into.</summary>
    public const string LegacyScenarioFolder = ".domain/_tests";

    private static readonly string[] StepKeywords = ["Given", "When", "Then", "And"];

    /// <summary>The GitHub anchor of a heading — the devbook checker's algorithm.</summary>
    public static string Slugify(string text) =>
        Whitespace().Replace(NotSlugCharacter().Replace(text.ToLowerInvariant().Trim(), string.Empty), "-");

    /// <summary>A page's stem: its file name without <c>.md</c>.</summary>
    public static string StemOf(string relativePath)
    {
        var name = relativePath.Replace('\\', '/').Split('/')[^1];
        return name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;
    }

    /// <summary>Collapse runs of whitespace to one space and trim.</summary>
    public static string Collapse(string text) => WhitespaceRun().Replace(text, " ").Trim();

    /// <summary>Whether the file-level <c>meta</c> block under a page's title says
    /// <c>type: scenario</c> — the one question the listing asks, without reading
    /// further than the block.</summary>
    public static bool IsScenarioPage(string markdown) =>
        Parse(markdown, string.Empty).IsScenario;

    public static ScenarioPage Parse(string markdown, string relativePath)
    {
        var lines = LineBreak().Split(markdown ?? string.Empty);
        string? title = null;
        IReadOnlyDictionary<string, ScenarioMetaValue> meta = new Dictionary<string, ScenarioMetaValue>(StringComparer.Ordinal);
        var parts = new List<MutablePart>();
        var shots = new List<ScenarioShotPoint>();

        string? fence = null;
        MutablePart? part = null;
        var inList = false;
        var blankSince = false;
        MutableStep? lastStep = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var marker = FenceMarker().Match(line);
            if (fence is not null)
            {
                if (marker.Success && marker.Groups[1].Value[0] == fence[0] && marker.Groups[1].Value.Length >= fence.Length) fence = null;
                continue;
            }

            if (marker.Success)
            {
                fence = marker.Groups[1].Value;
                continue;
            }

            var heading = Heading().Match(line);
            if (heading.Success && heading.Groups[1].Length == 1 && title is null)
            {
                title = heading.Groups[2].Value.Trim();
                var j = i + 1;
                while (j < lines.Length && lines[j].Trim().Length == 0) j++;
                if (j < lines.Length && MetaOpen().IsMatch(lines[j].Trim()))
                {
                    var body = new List<string>();
                    var k = j + 1;
                    while (k < lines.Length && lines[k].Trim() != "```") body.Add(lines[k++]);
                    meta = ParseMeta(body);
                    i = k;
                }

                continue;
            }

            if (heading.Success && heading.Groups[1].Length == 2)
            {
                part = new MutablePart(heading.Groups[2].Value.Trim(), Slugify(heading.Groups[2].Value), i + 1);
                parts.Add(part);
                inList = false;
                blankSince = false;
                lastStep = null;
                continue;
            }

            if (heading.Success)
            {
                inList = false;
                lastStep = null;
                continue;
            }

            if (line.Trim().Length == 0)
            {
                blankSince = true;
                continue;
            }

            var indented = Indented().IsMatch(line) || line.StartsWith('\t');
            var item = ListItem().Match(line);
            var topItem = item.Success && item.Groups[1].Length <= 1;
            var inStepList = false;

            if (part is not null && topItem)
            {
                var bold = BoldKeyword().Match(item.Groups[2].Value);
                var keyword = bold.Success && StepKeywords.Contains(bold.Groups[1].Value.Trim(), StringComparer.Ordinal)
                    ? bold.Groups[1].Value.Trim()
                    : null;
                lastStep = new MutableStep(keyword, Collapse(keyword is not null ? bold.Groups[2].Value : item.Groups[2].Value), i + 1);
                part.Steps.Add(lastStep);
                inList = true;
                inStepList = true;
            }
            else if (part is not null && inList && (indented || item.Success || !blankSince))
            {
                // A nested item, an indented line, or a lazy continuation belongs to the step above.
                if (lastStep is not null) lastStep.Text = Collapse($"{lastStep.Text} {ContinuationBullet().Replace(line, string.Empty, 1)}");
                inStepList = true;
            }
            else
            {
                inList = false;
                lastStep = null;
            }

            blankSince = false;

            foreach (Match match in ShotImage().Matches(MaskCodeSpans(line)))
            {
                var shot = new ScenarioShotPoint(match.Groups[2].Value, match.Groups[1].Value, i + 1, part?.Anchor, inStepList);
                shots.Add(shot);
                part?.Shots.Add(shot);

                // A label inside a step would be text of the step it sits in; take it back out.
                if (inStepList && lastStep is not null)
                {
                    lastStep.Text = Collapse(ReplaceFirst(lastStep.Text, Collapse(match.Value), string.Empty));
                }
            }
        }

        var isScenario = meta.TryGetValue("type", out var type) && string.Equals(type.Scalar, "scenario", StringComparison.Ordinal);
        var setup = new ScenarioSetup(
            Scalar(meta, "start"),
            List(meta, "actor"),
            List(meta, "data"),
            Profile(meta),
            List(meta, "flags"),
            List(meta, "settings"));

        return new ScenarioPage(
            relativePath.Replace('\\', '/'),
            StemOf(relativePath),
            title,
            isScenario,
            meta,
            setup,
            [.. parts.Select(p => new ScenarioPart(p.Title, p.Anchor, p.Line, [.. p.Steps.Select(s => new ScenarioStep(s.Keyword, s.Text, s.Line))], [.. p.Shots]))],
            shots);
    }

    /// <summary>A <c>meta</c> block body as fields, with the checker's scalar and
    /// <c>[a, b]</c> list syntax.</summary>
    public static IReadOnlyDictionary<string, ScenarioMetaValue> ParseMeta(IEnumerable<string> body)
    {
        var result = new Dictionary<string, ScenarioMetaValue>(StringComparer.Ordinal);
        foreach (var line in body)
        {
            var at = line.IndexOf(':', StringComparison.Ordinal);
            if (line.Trim().Length == 0 || at == -1) continue;
            result[line[..at].Trim()] = ParseScalar(line[(at + 1)..]);
        }

        return result;
    }

    private static ScenarioMetaValue ParseScalar(string raw)
    {
        var value = raw.Trim();
        if (value.Length == 0 || value == "null") return new ScenarioMetaValue(null, null);
        if (!(value.StartsWith('[') && value.EndsWith(']'))) return new ScenarioMetaValue(Unquote(value), null);

        var inner = value[1..^1].Trim();
        if (inner.Length == 0) return new ScenarioMetaValue(null, []);

        var entries = new List<string>();
        char? quote = null;
        var current = new System.Text.StringBuilder();
        foreach (var c in inner)
        {
            if (quote is not null)
            {
                if (c == quote) quote = null;
            }
            else if ((c == '"' || c == '\'') && current.ToString().Trim().Length == 0)
            {
                quote = c;
            }
            else if (c == ',')
            {
                entries.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        entries.Add(current.ToString());
        return new ScenarioMetaValue(null, [.. entries.Select(entry => Unquote(entry.Trim())).Where(entry => entry.Length > 0)]);
    }

    private static string Unquote(string value)
    {
        if (value.Length < 2) return value;
        var first = value[0];
        return (first == '"' || first == '\'') && value[^1] == first ? value[1..^1] : value;
    }

    private static string? Scalar(IReadOnlyDictionary<string, ScenarioMetaValue> meta, string key) =>
        meta.TryGetValue(key, out var value)
            ? value.Scalar ?? (value.List is { } list ? string.Join(",", list) : null)
            : null;

    /// <summary>The profile as parse.mjs reads it: a scalar, null when absent or
    /// empty; a list, its entries joined with commas — so <c>profile: []</c> is the
    /// empty profile, not <c>default</c>.</summary>
    private static string? Profile(IReadOnlyDictionary<string, ScenarioMetaValue> meta) =>
        !meta.TryGetValue("profile", out var value) ? null
        : value.List is { } list ? string.Join(",", list)
        : string.IsNullOrEmpty(value.Scalar) ? null
        : value.Scalar;

    private static IReadOnlyList<string> List(IReadOnlyDictionary<string, ScenarioMetaValue> meta, string key) =>
        meta.TryGetValue(key, out var value) ? value.AsList() : [];

    /// <summary>Replace code spans with spaces, so a quoted <c>![](shot:x)</c> is not
    /// read as an image.</summary>
    private static string MaskCodeSpans(string line) =>
        CodeSpan().Replace(line, span => new string(' ', span.Length));

    private static string ReplaceFirst(string text, string search, string replacement)
    {
        var at = text.IndexOf(search, StringComparison.Ordinal);
        return at < 0 ? text : string.Concat(text.AsSpan(0, at), replacement, text.AsSpan(at + search.Length));
    }

    private sealed class MutablePart(string title, string anchor, int line)
    {
        public string Title { get; } = title;
        public string Anchor { get; } = anchor;
        public int Line { get; } = line;
        public List<MutableStep> Steps { get; } = [];
        public List<ScenarioShotPoint> Shots { get; } = [];
    }

    private sealed class MutableStep(string? keyword, string text, int line)
    {
        public string? Keyword { get; } = keyword;
        public string Text { get; set; } = text;
        public int Line { get; } = line;
    }

    [GeneratedRegex(@"\r?\n")]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"^\s*(`{3,}|~{3,})")]
    private static partial Regex FenceMarker();

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^```meta\s*$")]
    private static partial Regex MetaOpen();

    [GeneratedRegex(@"^\s{2,}\S")]
    private static partial Regex Indented();

    // [0-9] rather than \d: JavaScript's \d is ASCII, .NET's is every Unicode digit.
    [GeneratedRegex(@"^( {0,3})(?:[-*+]|[0-9]+[.)])\s+(.*)$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^\*\*([^*]+)\*\*\s*(.*)$")]
    private static partial Regex BoldKeyword();

    [GeneratedRegex(@"^\s*(?:[-*+]|[0-9]+[.)])\s+")]
    private static partial Regex ContinuationBullet();

    [GeneratedRegex(@"!\[([^\]]*)\]\(\s*<?shot:([^)\s>]*)>?(?:\s+(?:""[^""]*""|'[^']*'))?\s*\)")]
    private static partial Regex ShotImage();

    [GeneratedRegex(@"(`+)[^`]*?\1")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"[^\p{L}\p{N}_\s-]")]
    private static partial Regex NotSlugCharacter();

    [GeneratedRegex(@"\s")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
