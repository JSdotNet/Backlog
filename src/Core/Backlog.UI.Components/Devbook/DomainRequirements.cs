using System.Text;
using System.Text.RegularExpressions;

namespace Backlog.UI.Components.Devbook;

/// <summary>One row of a scenario: its keyword and the clauses it holds, each a
/// bullet when the row joined more than one with "and".</summary>
/// <param name="Keyword"><c>Given</c>, <c>When</c> or <c>Then</c>.</param>
/// <param name="Clauses">The clauses as markdown, in the order written.</param>
public sealed record DomainScenarioStep(string Keyword, IReadOnlyList<string> Clauses);

/// <summary>A requirement's scenario as Given / When / Then rows.</summary>
/// <param name="Prose">What the scenario says outside its rows, when it says
/// anything.</param>
public sealed record DomainScenario(string Title, string Anchor, IReadOnlyList<DomainScenarioStep> Steps, string Prose);

/// <summary>One requirement as the Requirements page draws it.</summary>
/// <param name="Shall">The paragraph holding its SHALL sentence, as markdown, or
/// the empty string when the chapter states none.</param>
/// <param name="Prose">The rest of its own text.</param>
/// <param name="Tests">The tests its <c>tests</c> field links — what the devbook
/// database stores as the <c>tests</c> attribute.</param>
public sealed record DomainRequirement(
    string Key,
    string Title,
    string Reference,
    string? Status,
    string Shall,
    string Prose,
    IReadOnlyList<string> Tests,
    IReadOnlyList<DomainScenario> Scenarios)
{
    /// <summary>Whether a test is linked to it.</summary>
    public bool Proved => Tests.Count > 0;
}

/// <summary>A unit whose Always true tab holds the rules a feature's
/// requirements rest on.</summary>
/// <param name="DocumentPath">The domain file the unit is in, as the host spells
/// it.</param>
/// <param name="UnitKey">The unit's anchor in that file.</param>
/// <param name="Rules">How many rules its Always true tab holds, or 0 where the
/// files read hold none for it.</param>
public sealed record DomainAlwaysTrueLink(string Title, string DocumentPath, string UnitKey, int Rules = 0)
{
    /// <summary>The unit's reference: the file and its anchor.</summary>
    public string Reference => $"{DocumentPath}#{UnitKey}";

    /// <summary>The rule to land on, by its own reference — its invariants file
    /// and anchor — or <see langword="null"/> for the tab's top.</summary>
    public string? Rule { get; init; }

    /// <summary>The unit's rules the page shows in place, each linking to its
    /// own row on Always true. Empty where the page lists none.</summary>
    public IReadOnlyList<DomainRule> Listed { get; init; } = [];
}

/// <summary>One feature's requirements: a <c>##</c> chapter of a requirements
/// file.</summary>
/// <param name="Intro">The group's own text, with any sentence explaining
/// invariants taken out — <see cref="AlwaysTrue"/> lists them instead.</param>
/// <param name="Feature">The feature its <c>related</c> names.</param>
/// <param name="AlwaysTrue">The units whose rules the requirements rest on.</param>
public sealed record DomainRequirementGroup(
    string Key,
    string Title,
    string Reference,
    string Intro,
    DomainNamedChapter? Feature,
    IReadOnlyList<DomainRequirement> Requirements,
    IReadOnlyList<DomainAlwaysTrueLink> AlwaysTrue)
{
    /// <summary>How many requirements it holds, and how many are proved.</summary>
    public DomainProofTally Tally => new(Requirements.Count, Requirements.Count(requirement => requirement.Proved));
}

/// <summary>
/// Reads the Requirements page of the Domain view (local ADR 0037) from a
/// context's files: per feature, each requirement with its SHALL sentence and its
/// proof, each scenario as Given / When / Then rows. Pure, like
/// <see cref="DomainUnits"/>.
/// </summary>
public static partial class DomainRequirements
{
    /// <summary>The requirements of the feature <paramref name="feature"/>: those
    /// under a group whose <c>related</c> names it, those whose own
    /// <c>related</c> does, and those the feature's <paramref name="named"/>
    /// references name — a group standing for every requirement in it. In file
    /// order, each once, with the first group that matched.</summary>
    public static (string? Group, IReadOnlyList<DomainRequirement> Requirements) Of(
        string feature,
        IEnumerable<string> named,
        IEnumerable<DomainChapterFile> requirementFiles)
    {
        var target = DomainUnits.Normalize(feature);
        var names = named.Select(DomainUnits.Normalize).ToHashSet(StringComparer.Ordinal);
        string? group = null;
        var result = new List<DomainRequirement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in requirementFiles)
        {
            foreach (var chapter in file.Chapters) Walk(file, chapter, inherited: false);
        }
        return (group, result);

        void Walk(DomainChapterFile file, DomainChapter chapter, bool inherited)
        {
            var reference = DomainUnits.Normalize(file.ReferenceTo(chapter));
            var chosen = inherited
                || names.Contains(reference)
                || chapter.Related.Any(related => DomainUnits.Resolve(related, file.Path) == target);

            if (DomainUnits.IsRequirement(chapter))
            {
                if (chosen && seen.Add(reference)) result.Add(Requirement(file, chapter, chapter.Anchor));
                return;
            }

            if (chosen && !inherited) group ??= reference;
            foreach (var child in chapter.Children) Walk(file, child, chosen);
        }
    }

    /// <summary>
    /// The groups <paramref name="page"/> holds, one per feature, in file order.
    /// A requirement written straight under the file's title, with no group
    /// around it, is gathered into one group named by the title — the shape a
    /// split <c>requirements.&lt;name&gt;.md</c> may take.
    /// <paramref name="contextFiles"/> is every file of the context: the features
    /// files a group names, and the domain files whose units its Always true
    /// links open.
    /// </summary>
    public static IReadOnlyList<DomainRequirementGroup> Groups(DomainChapterFile page, IReadOnlyList<DomainChapterFile> contextFiles)
    {
        var files = contextFiles.Any(file => DomainUnits.Normalize(file.Path) == DomainUnits.Normalize(page.Path)) ? contextFiles : [.. contextFiles, page];
        var index = DomainChapterIndex.Of(files);
        var domainFiles = files.Where(file => DomainFeatures.KindOf(file.Path) == "domain" && !file.Path.EndsWith(".invariants.md", StringComparison.OrdinalIgnoreCase)).ToList();

        var groups = new List<DomainRequirementGroup>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        // Every unit's rules, read once for every group.
        var catalog = DomainRuleCatalog.Of(domainFiles, files);
        var loose = page.Chapters.Where(DomainUnits.IsRequirement).ToList();
        if (loose.Count > 0)
        {
            keys.Add(DomainUnits.Slug(page.Title));
            groups.Add(new DomainRequirementGroup(
                DomainUnits.Slug(page.Title),
                page.Title,
                DomainUnits.Normalize(page.Path),
                string.Empty,
                null,
                Requirements(page, loose),
                []));
        }

        foreach (var chapter in page.Chapters.Where(chapter => !DomainUnits.IsRequirement(chapter)))
        {
            var requirements = Requirements(page, Within(chapter));
            if (requirements.Count == 0 && chapter.Type != "requirements") continue;

            var key = chapter.Anchor;
            for (var n = 1; !keys.Add(key); n++) key = $"{chapter.Anchor}-{n}";

            var related = chapter.Related.Select(entry => DomainUnits.Resolve(entry, page.Path)).ToList();
            var feature = related.FirstOrDefault(entry => DomainFeatures.KindOf(DomainUnits.PathOf(entry)) is "features" or "skills" && entry.Contains('#'));
            var intro = OwnText(chapter.Prose);
            var alwaysTrue = AlwaysTrue(page, chapter, related, feature, intro, files, catalog);

            groups.Add(new DomainRequirementGroup(
                key,
                chapter.Title,
                DomainUnits.Normalize(page.ReferenceTo(chapter)),
                // The sentence goes only where a link takes its place: a group whose
                // rules belong to no unit of this context keeps saying where they are.
                alwaysTrue.Count > 0 ? WithoutInvariantExplanations(intro) : intro,
                feature is null ? null : index.Name(feature),
                requirements,
                alwaysTrue));
        }
        return groups;
    }

    /// <summary>The requirement chapters under a group, nested groupings
    /// included.</summary>
    private static IEnumerable<DomainChapter> Within(DomainChapter group) =>
        group.Children.SelectMany(child => DomainUnits.IsRequirement(child) ? [child] : Within(child));

    private static IReadOnlyList<DomainRequirement> Requirements(DomainChapterFile file, IEnumerable<DomainChapter> chapters)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<DomainRequirement>();
        foreach (var chapter in chapters)
        {
            var key = chapter.Anchor;
            for (var n = 1; !keys.Add(key); n++) key = $"{chapter.Anchor}-{n}";
            result.Add(Requirement(file, chapter, key));
        }
        return result;
    }

    private static DomainRequirement Requirement(DomainChapterFile file, DomainChapter chapter, string key)
    {
        var own = OwnText(chapter.Prose);
        var paragraphs = Paragraphs(own);
        var shall = paragraphs.FirstOrDefault(paragraph => paragraph.Contains("SHALL", StringComparison.Ordinal)) ?? string.Empty;
        var rest = string.Join("\n\n", paragraphs.Where(paragraph => !ReferenceEquals(paragraph, shall)));

        return new DomainRequirement(
            key,
            DomainUnits.RequirementTitle(chapter.Title),
            DomainUnits.Normalize(file.ReferenceTo(chapter)),
            chapter.Field("status"),
            shall,
            rest,
            chapter.ListField("tests"),
            [.. chapter.Children.Where(IsScenario).Select(Scenario)]);
    }

    private static bool IsScenario(DomainChapter chapter) =>
        chapter.Type is null or "scenario"
        && chapter.Title.StartsWith("Scenario:", StringComparison.OrdinalIgnoreCase);

    /// <summary>A scenario's rows: each <c>- **Given**</c>, <c>- **When**</c> and
    /// <c>- **Then**</c> line a row, an <c>- **And**</c> or <c>- **But**</c> line
    /// one more clause of the row above it, and a row's clauses joined by
    /// ", and " split apart.</summary>
    public static DomainScenario Scenario(DomainChapter chapter)
    {
        var steps = new List<(string Keyword, List<string> Clauses)>();
        var prose = new StringBuilder();
        foreach (var raw in OwnText(chapter.Prose).Split('\n'))
        {
            var match = StepLine().Match(raw);
            if (match.Success)
            {
                var keyword = Capitalized(match.Groups[1].Value);
                var clauses = SplitClauses(match.Groups[2].Value.Trim());
                if (keyword is "And" or "But" && steps.Count > 0)
                {
                    steps[^1].Clauses.AddRange(clauses);
                }
                else
                {
                    steps.Add((keyword is "And" or "But" ? "Given" : keyword, [.. clauses]));
                }
                continue;
            }

            // A wrapped line carries on the clause above it.
            if (steps.Count > 0 && raw.Length > 0 && char.IsWhiteSpace(raw[0]) && raw.Trim().Length > 0 && !raw.TrimStart().StartsWith('-'))
            {
                var clauses = steps[^1].Clauses;
                clauses[^1] = $"{clauses[^1]} {raw.Trim()}";
                continue;
            }

            if (raw.Trim().Length > 0) prose.Append(raw.Trim()).Append('\n');
        }

        var title = chapter.Title.StartsWith("Scenario:", StringComparison.OrdinalIgnoreCase)
            ? chapter.Title["Scenario:".Length..].Trim()
            : chapter.Title;
        return new DomainScenario(
            title,
            chapter.Anchor,
            [.. steps.Select(step => new DomainScenarioStep(step.Keyword, step.Clauses))],
            prose.ToString().Trim());
    }

    /// <summary>
    /// A SHALL paragraph cut around its first SHALL, so the page can set the word
    /// apart: the markdown before it and after it, each one line. Null where the
    /// paragraph is not running prose — a list, a quote, a table or a heading —
    /// or holds no SHALL outside a code span, link text or emphasis, and the page
    /// renders the paragraph whole.
    /// </summary>
    public static (string Before, string After)? SplitShall(string paragraph)
    {
        var raw = paragraph.Split('\n').Where(line => line.Trim().Length > 0).ToList();
        // Four spaces or a tab in front make a line indented code, not prose.
        if (raw.Count == 0 || raw.Any(line => line.StartsWith("    ", StringComparison.Ordinal) || line.StartsWith('\t'))) return null;
        var lines = raw.Select(line => line.Trim()).ToList();
        if (lines.Any(line => NotProse().IsMatch(line) || line.StartsWith('>'))) return null;

        var text = string.Join(' ', lines);
        var inCode = false;
        var inEmphasis = false;
        var inLinkText = false;
        var inLinkTarget = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '`') inCode = !inCode;
            else if (inCode) continue;
            else if (inLinkTarget)
            {
                if (ch == ')') inLinkTarget = false;
            }
            else if (ch == '*')
            {
                // A run — `*` or `**` — opens or closes one emphasis.
                while (i + 1 < text.Length && text[i + 1] == '*') i++;
                inEmphasis = !inEmphasis;
            }
            else if (ch == '[') inLinkText = true;
            else if (ch == ']')
            {
                inLinkText = false;
                if (i + 1 < text.Length && text[i + 1] == '(') inLinkTarget = true;
            }
            else if (!inEmphasis && !inLinkText
                && string.CompareOrdinal(text, i, "SHALL", 0, 5) == 0
                && (i == 0 || IsBoundary(text[i - 1]))
                && (i + 5 == text.Length || IsBoundary(text[i + 5])))
            {
                return (text[..i], text[(i + 5)..]);
            }
        }
        return null;

        // An underscore touching the word is emphasis written the other way, so
        // it is no boundary either.
        static bool IsBoundary(char ch) => !char.IsLetterOrDigit(ch) && ch != '_' && ch != '*';
    }

    /// <summary>A row's clauses: split at each ", and " outside a code span or
    /// emphasis. A bare "and" inside a clause — "<c>domain.md</c> and
    /// <c>model.md</c>" — names a list, not two clauses, and stays.</summary>
    public static IReadOnlyList<string> SplitClauses(string text)
    {
        var clauses = new List<string>();
        var start = 0;
        var inCode = false;
        var inEmphasis = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '`') inCode = !inCode;
            else if (!inCode && ch == '*') inEmphasis = !inEmphasis;
            else if (!inCode && !inEmphasis && string.CompareOrdinal(text, i, ", and ", 0, 6) == 0)
            {
                clauses.Add(text[start..i].Trim());
                start = i + 6;
                i += 5;
            }
        }
        clauses.Add(text[start..].Trim());
        return [.. clauses.Where(clause => clause.Length > 0)];
    }

    /// <summary>
    /// The units whose rules the group's requirements rest on, each with the
    /// rules the page shows in place. What the group, its requirements or its
    /// feature name in <c>related</c>, and each invariants link in the group's
    /// text, is first asked of <paramref name="catalog"/> for the rules about it
    /// (<see cref="DomainRuleCatalog.About(string)"/>). A reference no rule is
    /// about still reaches a unit with rules, all of them shown: the aggregate
    /// it names, a whole split file being its one aggregate, else the innermost
    /// holding the chapter it names. A link into
    /// <c>domain.&lt;name&gt;.invariants.md</c> is the aggregate of
    /// <c>domain.&lt;name&gt;.md</c>; one into <c>domain.invariants.md</c> the
    /// aggregate its anchor or its rule's <c>related</c> names.
    /// </summary>
    private static IReadOnlyList<DomainAlwaysTrueLink> AlwaysTrue(
        DomainChapterFile page,
        DomainChapter group,
        IReadOnlyList<string> related,
        string? feature,
        string intro,
        IReadOnlyList<DomainChapterFile> files,
        DomainRuleCatalog catalog)
    {
        // Each reference, and whether the rules about it are what it reaches.
        var named = new List<(string Reference, bool Precise)>(related.Select(entry => (entry, true)));
        named.AddRange(Within(group).SelectMany(requirement => requirement.Related.Select(entry => (DomainUnits.Resolve(entry, page.Path), true))));
        if (feature is not null)
        {
            foreach (var file in files.Where(file => DomainFeatures.KindOf(file.Path) == "features"))
            {
                var chapter = file.AllChapters().FirstOrDefault(candidate => DomainUnits.Normalize(file.ReferenceTo(candidate)) == feature);
                if (chapter is not null) named.AddRange(chapter.Related.Select(entry => (DomainUnits.Resolve(entry, file.Path), true)));
            }
        }

        foreach (Match link in InvariantsLink().Matches(intro))
        {
            var target = DomainUnits.Resolve(link.Groups[1].Value, page.Path);
            var path = DomainUnits.PathOf(target);
            var host = path[..^".invariants.md".Length] + ".md";
            var anchor = target.Length > path.Length + 1 ? target[(path.Length + 1)..] : null;

            var invariants = files.FirstOrDefault(file => DomainUnits.Normalize(file.Path) == path);
            var rule = anchor is null ? null : invariants?.AllChapters().FirstOrDefault(chapter => chapter.Anchor == anchor);

            // A link to a rule, or a group of rules, shows those rules alone.
            if (anchor is not null && catalog.About(target).Count > 0)
            {
                named.Add((target, true));
                continue;
            }
            if (rule is not null) named.AddRange(rule.Related.Select(entry => (DomainUnits.Resolve(entry, invariants!.Path), false)));
            named.Add((anchor is null ? host : $"{host}#{anchor}", false));
            named.Add((host, false));
        }

        var units = new List<(string Unit, DomainAlwaysTrueLink Link, List<DomainRule> Rules)>();
        foreach (var (reference, precise) in named)
        {
            var about = precise ? catalog.About(reference) : [];
            if (about.Count > 0)
            {
                foreach (var rule in about) Add(rule);
                continue;
            }

            foreach (var entry in Reached(catalog, reference))
            {
                foreach (var rule in entry.Rules) Add(rule);
            }
        }
        return [.. units.Select(unit => unit.Link with { Rules = unit.Rules.Count, Listed = catalog.InOrder(unit.Rules) })];

        void Add(DomainRule rule)
        {
            if (rule.Place is not { } place) return;
            var key = DomainUnits.Normalize(place.Reference);
            var index = units.FindIndex(unit => unit.Unit == key);
            if (index < 0)
            {
                units.Add((key, place with { Rule = null }, []));
                index = units.Count - 1;
            }
            if (!units[index].Rules.Contains(rule)) units[index].Rules.Add(rule);
        }
    }

    /// <summary>The units a reference no rule is about still reaches: the
    /// aggregate it names, a whole split file being its one aggregate, else the
    /// innermost holding the chapter it names.</summary>
    private static IEnumerable<DomainUnitRules> Reached(DomainRuleCatalog catalog, string reference)
    {
        var path = DomainUnits.PathOf(DomainUnits.Normalize(reference));
        var hash = reference.IndexOf('#');
        var anchor = hash < 0 ? null : reference[(hash + 1)..];
        var aggregates = catalog.Units
            .Where(entry => entry.Unit.Kind == DomainUnitKind.Aggregate && DomainUnits.Normalize(entry.Page.Path) == path)
            .ToList();
        if (anchor is null)
        {
            // A whole file is its aggregate only where it holds one.
            return aggregates.Count == 1 && DomainUnits.Of(aggregates[0].Page).Count(unit => unit.Kind == DomainUnitKind.Aggregate) == 1
                ? aggregates
                : [];
        }

        var named = aggregates.Where(entry => entry.Unit.Chapter.Anchor == anchor).Take(1).ToList();
        if (named.Count > 0) return named;
        var holder = aggregates.LastOrDefault(entry => entry.Unit.Chapter.Descendants().Any(chapter => chapter.Anchor == anchor));
        return holder is null ? [] : [holder];
    }

    /// <summary>A chapter's own text: its prose up to the first sub-heading.</summary>
    private static string OwnText(string prose)
    {
        var builder = new StringBuilder();
        var inFence = false;
        foreach (var line in prose.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)) inFence = !inFence;
            if (!inFence && trimmed.StartsWith('#')) break;
            builder.Append(line).Append('\n');
        }
        return builder.ToString().Trim();
    }

    private static List<string> Paragraphs(string text) =>
        [.. Regex.Split(text, @"\n\s*\n").Select(paragraph => paragraph.Trim()).Where(paragraph => paragraph.Length > 0)];

    /// <summary>
    /// <paramref name="intro"/> without the sentences that explain invariants —
    /// "The rules the registry holds are its invariants." — which the Always true
    /// link replaces. A paragraph left empty goes; a quote stays a quote.
    /// </summary>
    public static string WithoutInvariantExplanations(string intro)
    {
        var kept = new List<string>();
        foreach (var paragraph in Paragraphs(intro))
        {
            var lines = paragraph.Split('\n');
            var quoted = lines.All(line => line.TrimStart().StartsWith('>'));
            var bare = lines.Select(line => quoted ? line.TrimStart()[1..].Trim() : line.Trim()).ToList();
            var text = string.Join(' ', bare);

            // Only running prose is cut into sentences; a list or a table keeps its
            // shape, invariants named or not.
            if (!text.Contains("invariant", StringComparison.OrdinalIgnoreCase) || bare.Any(line => NotProse().IsMatch(line)))
            {
                kept.Add(paragraph);
                continue;
            }

            var sentences = new List<string>();
            var rest = text;
            while (rest.Length > 0)
            {
                var sentence = DomainUnits.SentenceOf(rest);
                if (!sentence.Contains("invariant", StringComparison.OrdinalIgnoreCase)) sentences.Add(sentence.Trim());
                rest = rest[sentence.Length..].TrimStart();
            }
            if (sentences.Count == 0) continue;

            var joined = string.Join(' ', sentences);
            kept.Add(quoted ? "> " + joined : joined);
        }
        return string.Join("\n\n", kept);
    }

    private static string Capitalized(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();

    [GeneratedRegex(@"^\s*[-*]\s+\*\*(Given|When|Then|And|But)\*\*\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex StepLine();

    [GeneratedRegex(@"^(?:[-*+]\s|\d+[.)]\s|\||#)")]
    private static partial Regex NotProse();

    [GeneratedRegex(@"\]\(([^)\s]*\.invariants\.md(?:#[^)\s]*)?)\)", RegexOptions.IgnoreCase)]
    private static partial Regex InvariantsLink();
}
