using System.Text.RegularExpressions;

namespace Backlog.Infrastructure.Devbook.Scenarios;

/// <summary>
/// One scenarios file, read: a bounded context's <c>scenarios.md</c>, or a split
/// <c>scenarios.&lt;name&gt;.md</c>, whose <c>##</c> chapters are its journeys.
/// <para>
/// A port of devbook's <c>parseScenariosFile</c> (<c>.devbook/_tools/scenarios/parse.mjs</c>),
/// line for line where it decides anything the signature hashes: the signature is only
/// worth comparing because every implementation reads a journey the same way, and
/// <c>scenario-page.vector.json</c> beside that file is what proves this one does.
/// </para>
/// </summary>
/// <param name="Meta">The file's own block, under its <c>#</c> title.</param>
/// <param name="Journeys">Every <c>##</c> chapter, in file order, whether or not its
/// block says <c>type: scenario</c>; <see cref="Scenarios"/> is the ones that do.</param>
/// <param name="Shots">Every screenshot point in the file, in document order.</param>
public sealed record ScenariosFile(
    string Path,
    string? Title,
    IReadOnlyDictionary<string, ScenarioMetaValue> Meta,
    IReadOnlyList<ScenarioJourney> Journeys,
    IReadOnlyList<ScenarioShotPoint> Shots)
{
    /// <summary>The journeys: the <c>##</c> chapters typed <c>scenario</c>.</summary>
    public IReadOnlyList<ScenarioJourney> Scenarios => [.. Journeys.Where(journey => journey.IsScenario)];

    /// <summary>Whether the whole file is typed <c>scenario</c> — a journey page of the
    /// contract before devbook's 32, which the <c>032-scenarios-md</c> migration folds
    /// into its context's <c>scenarios.md</c>. Such a page holds no journey.</summary>
    public bool LegacyPage => Meta.TryGetValue("type", out var type) && string.Equals(type.Scalar, "scenario", StringComparison.Ordinal);
}

/// <summary>
/// One journey, read: a <c>##</c> chapter of a scenarios file, its own <c>meta</c>
/// block with the six setup fields, its <c>###</c> parts with their steps, and the
/// <c>shot:&lt;label&gt;</c> screenshot points between the step lists.
/// </summary>
/// <param name="Path">The scenarios file the journey is written in, repository-relative.</param>
/// <param name="Slug">The journey heading's anchor.</param>
/// <param name="IsScenario">Whether the journey's block says <c>type: scenario</c>.</param>
public sealed record ScenarioJourney(
    string Path,
    string Slug,
    string Title,
    int Line,
    bool IsScenario,
    IReadOnlyDictionary<string, ScenarioMetaValue> Meta,
    ScenarioSetup Setup,
    IReadOnlyList<ScenarioPart> Parts,
    IReadOnlyList<ScenarioShotPoint> Shots)
{
    /// <summary>The journey's address: <c>&lt;scenarios file&gt;#&lt;journey&gt;</c>, what a
    /// spec's <c>// scenario:</c> header and a version 3 run's <c>journey</c> name.</summary>
    public string Address => $"{Path}#{Slug}";

    /// <summary>
    /// Where the journey's runs live, repository-relative: its scenarios file without
    /// <c>.md</c>, then the journey's anchor — <c>scenarios.returns.md#return-a-parcel</c>
    /// runs into <c>scenarios.returns/return-a-parcel/</c> (parse.mjs <c>runFolderOf</c>).
    /// </summary>
    public string RunFolder =>
        $"{(Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? Path[..^3] : Path)}/{Slug}";

    /// <summary>The journey's <c>status</c> — <c>draft</c> or <c>proposed</c> — or null:
    /// the journey is in force.</summary>
    public string? Status => Meta.TryGetValue("status", out var status) ? status.Scalar : null;

    /// <summary>Every label in the journey, in document order, each once.</summary>
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

/// <summary>One <c>###</c> part of a journey: a title the actor reaches, its anchor,
/// its steps and its screenshot points.</summary>
public sealed record ScenarioPart(string Title, string Anchor, int Line, IReadOnlyList<ScenarioStep> Steps, IReadOnlyList<ScenarioShotPoint> Shots);

/// <summary>One step. <see cref="Keyword"/> is null for a list item that opens with
/// none of <c>Given</c>, <c>When</c>, <c>Then</c>, <c>And</c>.</summary>
public sealed record ScenarioStep(string? Keyword, string Text, int Line);

/// <summary>A <c>![caption](shot:label)</c> in a scenarios file. <see cref="Part"/> is
/// the anchor of the part it sits in, null above its journey's first part.</summary>
public sealed record ScenarioShotPoint(string Label, string Caption, int Line, string? Part, bool InStepList);

/// <summary>A <c>meta</c> value: a scalar, or a <c>[a, b]</c> list.</summary>
public sealed record ScenarioMetaValue(string? Scalar, IReadOnlyList<string>? List)
{
    public IReadOnlyList<string> AsList() =>
        List ?? (string.IsNullOrEmpty(Scalar) ? [] : [Scalar]);
}

/// <summary>The parser. See <see cref="ScenariosFile"/>.</summary>
public static partial class ScenarioPageParser
{
    /// <summary>Where a repository keeps its profiles and data sets. A journey's runs
    /// live beside its scenarios file instead (<see cref="ScenarioJourney.RunFolder"/>).</summary>
    public const string ScenarioFolder = ".devbook/scenarios";

    /// <summary>The classic layout's scenario folder, which spec-manager's
    /// <c>.test</c> tooling keeps its data sets in.</summary>
    public const string LegacyScenarioFolder = ".domain/_tests";

    private static readonly string[] StepKeywords = ["Given", "When", "Then", "And"];

    /// <summary>The GitHub anchor of a heading — the devbook checker's algorithm.</summary>
    public static string Slugify(string text) =>
        Whitespace().Replace(NotSlugCharacter().Replace(text.ToLowerInvariant().Trim(), string.Empty), "-");

    /// <summary>Collapse runs of whitespace to one space and trim.</summary>
    public static string Collapse(string text) => WhitespaceRun().Replace(text, " ").Trim();

    /// <summary>Whether a file is named as a scenarios file: <c>scenarios.md</c>, or a
    /// split <c>scenarios.&lt;name&gt;.md</c> (parse.mjs <c>isScenariosPath</c>, without
    /// its folder depth, which differs between the devbook and the root layout).</summary>
    public static bool IsScenariosFileName(string path) =>
        ScenariosFileName().IsMatch(path.Replace('\\', '/').Split('/')[^1]);

    /// <summary>
    /// Reads one scenarios file. The <c>#</c> title carries the file's block; each
    /// <c>##</c> opens a journey with its own block after optional blank lines; a
    /// <c>###</c> is a part only inside a journey; a screenshot point belongs to the
    /// journey and part it sits in.
    /// </summary>
    public static ScenariosFile ParseFile(string markdown, string relativePath)
    {
        var path = relativePath.Replace('\\', '/');
        var lines = LineBreak().Split(markdown ?? string.Empty);
        string? title = null;
        IReadOnlyDictionary<string, ScenarioMetaValue> meta = new Dictionary<string, ScenarioMetaValue>(StringComparer.Ordinal);
        var journeys = new List<MutableJourney>();
        var shots = new List<ScenarioShotPoint>();

        string? fence = null;
        MutableJourney? journey = null;
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
                (meta, i) = ReadMetaAfter(lines, i);
                continue;
            }

            if (heading.Success && heading.Groups[1].Length == 2)
            {
                var journeyTitle = heading.Groups[2].Value.Trim();
                var journeyLine = i + 1;
                (var journeyMeta, i) = ReadMetaAfter(lines, i);
                journey = new MutableJourney(journeyTitle, Slugify(journeyTitle), journeyLine, journeyMeta);
                journeys.Add(journey);
                part = null;
                inList = false;
                blankSince = false;
                lastStep = null;
                continue;
            }

            if (heading.Success && heading.Groups[1].Length == 3 && journey is not null)
            {
                part = new MutablePart(heading.Groups[2].Value.Trim(), Slugify(heading.Groups[2].Value), i + 1);
                journey.Parts.Add(part);
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
                journey?.Shots.Add(shot);
                part?.Shots.Add(shot);

                // A label inside a step would be text of the step it sits in; take it back out.
                if (inStepList && lastStep is not null)
                {
                    lastStep.Text = Collapse(ReplaceFirst(lastStep.Text, Collapse(match.Value), string.Empty));
                }
            }
        }

        return new ScenariosFile(path, title, meta, [.. journeys.Select(entry => entry.Build(path))], shots);
    }

    /// <summary>The <c>meta</c> block after a heading, past any blank lines, and the
    /// index of its closing line — or no fields and the heading's own index when the
    /// heading has none.</summary>
    private static (IReadOnlyDictionary<string, ScenarioMetaValue> Meta, int End) ReadMetaAfter(string[] lines, int i)
    {
        var j = i + 1;
        while (j < lines.Length && lines[j].Trim().Length == 0) j++;
        if (j >= lines.Length || !MetaOpen().IsMatch(lines[j].Trim())) return (new Dictionary<string, ScenarioMetaValue>(StringComparer.Ordinal), i);

        var body = new List<string>();
        var k = j + 1;
        while (k < lines.Length && lines[k].Trim() != "```") body.Add(lines[k++]);
        return (ParseMeta(body), k);
    }

    private static ScenarioSetup SetupOf(IReadOnlyDictionary<string, ScenarioMetaValue> meta) =>
        new(
            Scalar(meta, "start"),
            List(meta, "actor"),
            List(meta, "data"),
            Profile(meta),
            List(meta, "flags"),
            List(meta, "settings"));

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

    private sealed class MutableJourney(string title, string slug, int line, IReadOnlyDictionary<string, ScenarioMetaValue> meta)
    {
        public List<MutablePart> Parts { get; } = [];
        public List<ScenarioShotPoint> Shots { get; } = [];

        public ScenarioJourney Build(string path) =>
            new(
                path,
                slug,
                title,
                line,
                meta.TryGetValue("type", out var type) && string.Equals(type.Scalar, "scenario", StringComparison.Ordinal),
                meta,
                SetupOf(meta),
                [.. Parts.Select(p => new ScenarioPart(p.Title, p.Anchor, p.Line, [.. p.Steps.Select(s => new ScenarioStep(s.Keyword, s.Text, s.Line))], [.. p.Shots]))],
                [.. Shots]);
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

    [GeneratedRegex(@"^scenarios(?:\.[^./]+)?\.md$", RegexOptions.IgnoreCase)]
    private static partial Regex ScenariosFileName();

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
