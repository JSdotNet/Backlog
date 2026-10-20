namespace Backlog.UI.Components.Devbook;

/// <summary>A chapter another chapter names, as a line on a page reads it: its
/// heading where the context holds it, and the reference that opens it.</summary>
/// <param name="Title">The chapter's heading as written, or — for a chapter the
/// context's files do not hold — its anchor in words.</param>
/// <param name="Reference">The path and anchor as resolved, spelled the way the
/// host spells the context's own files: <c>.devbook/domain/tasks/domain.md#task</c>.</param>
/// <param name="Known">Whether the chapter is one of the context's own, so the
/// page can open it in place rather than through the Devbook menu.</param>
public sealed record DomainNamedChapter(string Title, string Reference, bool Known);

/// <summary>What gates or configures a feature: the <c>feature-flag</c> or
/// <c>setting</c> its block names.</summary>
public enum DomainFeatureGateKind
{
    /// <summary>A switch decided at release.</summary>
    Flag,

    /// <summary>A value a person chooses.</summary>
    Setting
}

/// <summary>The flag or setting a feature hangs on.</summary>
public sealed record DomainFeatureGate(DomainFeatureGateKind Kind, DomainNamedChapter Chapter)
{
    /// <summary>What the gate is, in a word: <c>Flag</c> or <c>Setting</c>.</summary>
    public string Word => Kind == DomainFeatureGateKind.Flag ? "Flag" : "Setting";
}

/// <summary>How many requirements a feature has, and how many of them a test is
/// linked to.</summary>
public sealed record DomainProofTally(int Count, int Proved)
{
    public static readonly DomainProofTally None = new(0, 0);
}

/// <summary>
/// One feature as the Features page draws it (local ADR 0037): a card in the
/// column of what it builds on, and the detail picking it opens.
/// </summary>
/// <param name="Key">The chapter's anchor, unique on the page.</param>
/// <param name="Reference">The feature's own reference, beneath the domain
/// folder.</param>
/// <param name="Status">The block's <c>status</c>, or <see langword="null"/>.</param>
/// <param name="Depth">How many features of the context it builds on, end to
/// end: 0 for one that builds on none of them.</param>
public sealed record DomainFeature(
    string Key,
    string Title,
    string Reference,
    string? Status,
    string Summary,
    IReadOnlyList<DomainNamedChapter> SubFeatures,
    DomainFeatureGate? Gate,
    IReadOnlyList<DomainNamedChapter> BuildsOn,
    IReadOnlyList<DomainNamedChapter> ModelChapters,
    DomainNamedChapter? RequirementsGroup,
    DomainProofTally Requirements,
    int Depth,
    DomainChapter Chapter)
{
    /// <summary>Proposed and not yet agreed: drawn dashed.</summary>
    public bool IsProposed => string.Equals(Status, "proposed", StringComparison.OrdinalIgnoreCase);

    /// <summary>Out of force: struck through, after the rest of its column.</summary>
    public bool IsDeprecated => string.Equals(Status, "deprecated", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One column of the Features page: the features that build on the
/// same number of the context's features.</summary>
public sealed record DomainFeatureColumn(int Depth, IReadOnlyList<DomainFeature> Features)
{
    /// <summary>What the column's heading says.</summary>
    public string Title => Depth switch
    {
        0 => "Builds on nothing here",
        1 => "Builds on one feature",
        _ => $"Builds on {Depth} levels"
    };
}

/// <summary>
/// Reads the Features page of the Domain view (local ADR 0037) from a context's
/// files. Pure: text in, records out, so the storybook draws the page the app
/// draws from a repository.
/// <para>
/// Everything is what the chapters already say (§5): headings as written, a
/// feature's first sentence as its summary, <c>depends-on</c> for its column,
/// <c>feature-flag</c> or <c>setting</c> for what gates it, <c>related</c> for its
/// model chapters, and each requirement's own <c>tests</c> field — the field the
/// devbook database stores as the <c>tests</c> attribute — for whether it is
/// proved.
/// </para>
/// </summary>
public static class DomainFeatures
{
    /// <summary>
    /// The features <paramref name="page"/> holds, in columns by what they build
    /// on. <paramref name="contextFiles"/> is every file of the context the page
    /// is in — its other features files, so a split file's feature still counts
    /// what the base file's features build on, and its requirements, domain, model
    /// and context files, which name its requirements, model chapters and gate.
    /// </summary>
    public static IReadOnlyList<DomainFeatureColumn> Columns(DomainChapterFile page, IReadOnlyList<DomainChapterFile> contextFiles)
    {
        var features = Read(page, contextFiles);
        return
        [
            .. features
                .GroupBy(feature => feature.Depth)
                .OrderBy(group => group.Key)
                .Select(group => new DomainFeatureColumn(
                    group.Key,
                    [.. group.Where(feature => !feature.IsDeprecated), .. group.Where(feature => feature.IsDeprecated)]))
        ];
    }

    /// <summary>The features <paramref name="page"/> holds, in file order.</summary>
    public static IReadOnlyList<DomainFeature> Read(DomainChapterFile page, IReadOnlyList<DomainChapterFile> contextFiles)
    {
        var files = contextFiles.Any(file => SamePath(file.Path, page.Path)) ? contextFiles : [.. contextFiles, page];
        var index = DomainChapterIndex.Of(files);

        // Every feature of the context, so a depth counts the whole chain.
        var everyFeature = files
            .SelectMany(file => file.AllChapters().Where(IsFeature).Select(chapter => (File: file, Chapter: chapter)))
            .GroupBy(entry => DomainUnits.Normalize(entry.File.ReferenceTo(entry.Chapter)), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);

        var requirementFiles = files.Where(file => KindOf(file.Path) == "requirements").ToList();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<DomainFeature>();
        foreach (var chapter in page.AllChapters().Where(IsFeature))
        {
            var key = chapter.Anchor;
            for (var n = 1; !keys.Add(key); n++) key = $"{chapter.Anchor}-{n}";

            var reference = DomainUnits.Normalize(page.ReferenceTo(chapter));
            var requirements = DomainRequirements.Of(reference, chapter.Related.Select(related => DomainUnits.Resolve(related, page.Path)), requirementFiles);

            result.Add(new DomainFeature(
                key,
                chapter.Title,
                reference,
                chapter.Field("status"),
                chapter.Summary,
                [.. chapter.Descendants().Where(child => child.Type == "sub-feature").Select(child => new DomainNamedChapter(child.Title, page.ReferenceTo(child), Known: true))],
                Gate(chapter, page.Path, index),
                [.. chapter.ListField("depends-on").Select(entry => index.Name(DomainUnits.Resolve(entry, page.Path)))],
                [
                    .. chapter.Related
                        .Select(related => DomainUnits.Resolve(related, page.Path))
                        .Where(IsModelChapter)
                        .Distinct(StringComparer.Ordinal)
                        .Select(index.Name)
                        // The aggregate's domain chapter and its model section share a
                        // heading; the reader is shown the name once, the first written.
                        .DistinctBy(chapter => chapter.Title, StringComparer.OrdinalIgnoreCase)
                ],
                requirements.Group is { } group ? index.Name(group) : null,
                new DomainProofTally(requirements.Requirements.Count, requirements.Requirements.Count(requirement => requirement.Proved)),
                Depth(reference, everyFeature, depths, []),
                chapter));
        }
        return result;
    }

    private static DomainFeatureGate? Gate(DomainChapter chapter, string path, DomainChapterIndex index)
    {
        if (chapter.ListField("feature-flag").FirstOrDefault() is { } flag)
            return new DomainFeatureGate(DomainFeatureGateKind.Flag, index.Name(DomainUnits.Resolve(flag, path)));
        if (chapter.ListField("setting").FirstOrDefault() is { } setting)
            return new DomainFeatureGate(DomainFeatureGateKind.Setting, index.Name(DomainUnits.Resolve(setting, path)));
        return null;
    }

    /// <summary>How many of the context's features <paramref name="reference"/>
    /// builds on, end to end. A feature in another context, or one the context
    /// does not hold, adds nothing; a cycle stops where it closes.</summary>
    private static int Depth(
        string reference,
        IReadOnlyDictionary<string, (DomainChapterFile File, DomainChapter Chapter)> features,
        Dictionary<string, int> depths,
        HashSet<string> visiting)
    {
        if (depths.TryGetValue(reference, out var known)) return known;
        if (!features.TryGetValue(reference, out var feature) || !visiting.Add(reference)) return 0;

        var depth = 0;
        foreach (var entry in feature.Chapter.ListField("depends-on"))
        {
            var target = DomainUnits.Resolve(entry, feature.File.Path);
            if (!features.ContainsKey(target) || visiting.Contains(target)) continue;
            depth = Math.Max(depth, Depth(target, features, depths, visiting) + 1);
        }

        visiting.Remove(reference);
        depths[reference] = depth;
        return depth;
    }

    private static bool IsFeature(DomainChapter chapter) => chapter.Type == "feature";

    /// <summary>Whether a reference names a chapter of the model: the context's
    /// <c>domain.md</c> or <c>model.md</c>, or a split of either — never its
    /// invariants, which are Always true's.</summary>
    private static bool IsModelChapter(string reference) =>
        KindOf(DomainUnits.PathOf(reference)) is "domain" or "model"
        && !DomainUnits.PathOf(reference).EndsWith(".invariants.md", StringComparison.Ordinal);

    /// <summary>The kind a file's name says it is: the part of its name before
    /// the first dot — <c>features</c> for <c>features.checkout.md</c>.</summary>
    internal static string KindOf(string path)
    {
        var name = DomainUnits.Normalize(path);
        var slash = name.LastIndexOf('/');
        if (slash >= 0) name = name[(slash + 1)..];
        var dot = name.IndexOf('.');
        return dot < 0 ? name : name[..dot];
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(DomainUnits.Normalize(left), DomainUnits.Normalize(right), StringComparison.Ordinal);
}

/// <summary>Every chapter of a context's files by its reference, so a reference
/// one chapter writes reads as the heading of the chapter it names.</summary>
internal sealed class DomainChapterIndex
{
    private readonly Dictionary<string, string> _titles;
    private readonly string _prefix;

    private DomainChapterIndex(Dictionary<string, string> titles, string prefix) => (_titles, _prefix) = (titles, prefix);

    public static DomainChapterIndex Of(IEnumerable<DomainChapterFile> files)
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        string? prefix = null;
        foreach (var file in files)
        {
            prefix ??= PrefixOf(file.Path);
            titles.TryAdd(DomainUnits.Normalize(file.Path), file.Title);
            foreach (var chapter in file.AllChapters())
            {
                titles.TryAdd(DomainUnits.Normalize(file.ReferenceTo(chapter)), DomainUnits.RequirementTitle(chapter.Title));
            }
        }
        return new DomainChapterIndex(titles, prefix ?? string.Empty);
    }

    /// <summary>The domain folder as <paramref name="path"/> spells it:
    /// <c>.devbook/domain/</c> or <c>.domain/</c>, or nothing.</summary>
    private static string PrefixOf(string path)
    {
        var text = path.Replace('\\', '/').TrimStart('/');
        foreach (var prefix in new[] { ".devbook/domain/", ".domain/" })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return text[..prefix.Length];
        }
        return string.Empty;
    }

    /// <summary>A reference beneath the domain folder, spelled as the context's
    /// files are; one outside it — <c>.devbook/arc42/…</c> — as it is.</summary>
    public string Spell(string reference) => reference.StartsWith('.') ? reference : _prefix + reference;

    /// <summary>The chapter <paramref name="reference"/> names: its heading when
    /// the context holds it, else its anchor — or its file's name — in words.</summary>
    public DomainNamedChapter Name(string reference) =>
        _titles.TryGetValue(reference, out var title) && title.Length > 0
            ? new DomainNamedChapter(title, Spell(reference), Known: true)
            : new DomainNamedChapter(Words(reference), Spell(reference), Known: false);

    private static string Words(string reference)
    {
        var hash = reference.IndexOf('#');
        var text = hash >= 0 ? reference[(hash + 1)..] : reference;
        if (hash < 0)
        {
            var slash = text.LastIndexOf('/');
            if (slash >= 0) text = text[(slash + 1)..];
            if (text.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) text = text[..^3];
        }
        text = text.Replace('-', ' ').Replace('.', ' ').Trim();
        return text.Length == 0 ? reference : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
