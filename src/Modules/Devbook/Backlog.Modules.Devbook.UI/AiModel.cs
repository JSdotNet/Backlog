using Backlog.SharedKernel.Devbook;
using Backlog.SharedKernel.Markdown;
using Backlog.SharedKernel.Metadata;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The AI adoption record read as the delivery-path model — what the three views
/// <c>devbook-ai.md</c> ("The loop picture") describes are drawn from: adoption,
/// cohesion and model.
/// <para>
/// Every element is one field on a chapter and nothing is authored twice. The
/// seven stages and the three layers are the fixed vocabulary; a usage sits at
/// each stage its <c>stage</c> lists and in each layer its <c>layer</c> lists,
/// in the sub-stages its <c>substage</c> names; it hands work on through
/// <c>feeds</c> and leans on others through <c>related</c>. A stage or layer
/// with no usage stays in the model, empty — the emptiness is the finding.
/// </para>
/// </summary>
/// <param name="Stages">The seven stages, in path order, every one present.</param>
/// <param name="Layers">The three layers beneath the path, every one present.</param>
/// <param name="Usages">Every chapter carrying a block, in reading order.</param>
/// <param name="Throughout">The concepts with neither <c>stage</c> nor
/// <c>layer</c>: they apply throughout and sit in no row.</param>
/// <param name="Feeds">The <c>feeds</c> hand-offs whose target is a usage.</param>
/// <param name="UnresolvedFeeds">The <c>feeds</c> entries that name no usage,
/// or are not written <c>"&lt;path&gt;#&lt;anchor&gt;: &lt;label&gt;"</c> —
/// reported, never dropped.</param>
/// <param name="Supports">The <c>related</c> links, as support edges.</param>
/// <param name="Unplaced">The chapters other than a concept with neither a
/// stage nor a layer on the vocabulary: off the picture, and reported.</param>
public sealed record AiModel(
    IReadOnlyList<AiModelStage> Stages,
    IReadOnlyList<AiModelLayer> Layers,
    IReadOnlyList<AiModelUsage> Usages,
    IReadOnlyList<AiModelUsage> Throughout,
    IReadOnlyList<AiModelFeed> Feeds,
    IReadOnlyList<AiModelFeed> UnresolvedFeeds,
    IReadOnlyList<AiModelSupport> Supports,
    IReadOnlyList<AiModelUsage> Unplaced)
{
    /// <summary>The delivery path's seven, in order, with their display names:
    /// the first four before the pull request, the last three after it.</summary>
    public static IReadOnlyList<(string Key, string Name)> StageVocabulary { get; } =
    [
        ("decide", "Decide"),
        ("specify", "Specify & plan"),
        ("code", "Code"),
        ("verify", "Verify"),
        ("integrate", "Integrate"),
        ("release", "Release & deploy"),
        ("operate", "Operate & feedback")
    ];

    /// <summary>How many stages sit before the pull request.</summary>
    public const int StagesBeforePullRequest = 4;

    /// <summary>The three layers underneath the path, with their display names.</summary>
    public static IReadOnlyList<(string Key, string Name)> LayerVocabulary { get; } =
    [
        ("thread", "Spec ↔ code thread"),
        ("upkeep", "Upkeep loop"),
        ("harness", "Agent harness")
    ];

    /// <summary>The <c>.tech</c> ladder ranked by how far a usage has got, for a
    /// stage's own shade: adopted outranks trial outranks candidate, and a usage
    /// on hold or retired only shades a stage nothing live sits at.</summary>
    private static readonly string[] RungsHighestFirst = ["adopted", "trial", "candidate", "hold", "retired"];

    public static AiModel From(IEnumerable<DocumentDevbookFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var stageKeys = StageVocabulary.Select(stage => stage.Key).ToHashSet(StringComparer.Ordinal);
        var layerKeys = LayerVocabulary.Select(layer => layer.Key).ToHashSet(StringComparer.Ordinal);

        var byStage = StageVocabulary.ToDictionary(stage => stage.Key, _ => new List<AiModelUsage>(), StringComparer.Ordinal);
        var byLayer = LayerVocabulary.ToDictionary(layer => layer.Key, _ => new List<AiModelUsage>(), StringComparer.Ordinal);
        var bySubstage = new Dictionary<string, List<AiModelUsage>>(StringComparer.Ordinal);
        var substageOrder = new List<string>();

        var usages = new List<AiModelUsage>();
        var throughout = new List<AiModelUsage>();
        var unplaced = new List<AiModelUsage>();
        var pending = new List<(AiModelUsage Usage, MetadataRecord Meta)>();

        foreach (var file in files)
        {
            foreach (var section in file.Sections)
            {
                // A section with no block is prose — the root's own chapters — and
                // not a usage at all.
                if (section.Meta.IsEmpty) continue;

                // A value off the vocabulary places the chapter nowhere rather than
                // inventing an eighth stage; the checker is what reports the typo.
                var stages = Words(section.Meta, "stage").Where(stageKeys.Contains).ToList();
                var layers = Words(section.Meta, "layer").Where(layerKeys.Contains).ToList();
                var substages = Words(section.Meta, "substage")
                    .Where(entry => SubstageStage(entry) is { } stage && stages.Contains(stage, StringComparer.Ordinal))
                    .ToList();

                var usage = AiModelUsage.From(file, section, stages, substages, layers);
                usages.Add(usage);
                pending.Add((usage, section.Meta));

                foreach (var stage in stages) byStage[stage].Add(usage);
                foreach (var layer in layers) byLayer[layer].Add(usage);
                foreach (var substage in substages)
                {
                    if (!bySubstage.TryGetValue(substage, out var list))
                    {
                        bySubstage[substage] = list = [];
                        substageOrder.Add(substage);
                    }

                    list.Add(usage);
                }

                if (stages.Count == 0 && layers.Count == 0)
                {
                    (string.Equals(section.Meta.Type, "concept", StringComparison.OrdinalIgnoreCase) ? throughout : unplaced).Add(usage);
                }
            }
        }

        var feeds = new List<AiModelFeed>();
        var unresolved = new List<AiModelFeed>();
        var supports = new List<AiModelSupport>();

        foreach (var (usage, meta) in pending)
        {
            foreach (var entry in meta.Extra.GetValueOrDefault("feeds", []).Distinct(StringComparer.Ordinal))
            {
                var feed = Feed(usage, entry, usages);
                (feed.IsResolved ? feeds : unresolved).Add(feed);
            }

            foreach (var reference in meta.Related.DistinctBy(reference => reference.Raw, StringComparer.Ordinal))
            {
                supports.Add(new AiModelSupport(usage, Resolve(reference, usages), reference));
            }
        }

        var modelStages = StageVocabulary
            .Select((stage, index) => new AiModelStage(
                stage.Key,
                stage.Name,
                index < StagesBeforePullRequest,
                [.. substageOrder
                    .Where(substage => SubstageStage(substage) == stage.Key)
                    .Select(substage => new AiModelSubstage(substage, substage[(stage.Key.Length + 1)..], bySubstage[substage]))],
                byStage[stage.Key],
                HighestRung(byStage[stage.Key])))
            .ToList();

        var modelLayers = LayerVocabulary
            .Select(layer => new AiModelLayer(layer.Key, layer.Name, byLayer[layer.Key], HighestRung(byLayer[layer.Key])))
            .ToList();

        return new AiModel(modelStages, modelLayers, usages, throughout, feeds, unresolved, supports, unplaced);
    }

    /// <summary>A list field's entries, trimmed, lower-cased and de-duplicated in
    /// the order written.</summary>
    private static IReadOnlyList<string> Words(MetadataRecord meta, string field) =>
        meta.Extra.TryGetValue(field, out var values)
            ? [.. values.Select(value => value.Trim().ToLowerInvariant()).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal)]
            : [];

    /// <summary>The stage before the slash of a <c>&lt;stage&gt;/&lt;word&gt;</c>
    /// entry, or null when the entry is not one.</summary>
    private static string? SubstageStage(string entry)
    {
        var slash = entry.IndexOf('/');
        return slash > 0 && slash < entry.Length - 1 && entry.IndexOf('/', slash + 1) < 0 ? entry[..slash] : null;
    }

    /// <summary>One <c>feeds</c> entry: the reference before the first
    /// <c>": "</c>, the label after it — the generator's <c>parseFeed</c> split.</summary>
    private static AiModelFeed Feed(AiModelUsage from, string entry, IReadOnlyList<AiModelUsage> usages)
    {
        var at = entry.IndexOf(": ", StringComparison.Ordinal);
        if (at <= 0) return new AiModelFeed(from, null, entry, null);

        var target = entry[..at].Trim();
        var label = entry[(at + 2)..].Trim();
        if (target.Length == 0 || label.Length == 0 || target.Any(char.IsWhiteSpace)) return new AiModelFeed(from, null, target, label.Length == 0 ? null : label);

        var to = DevbookReference.Parse(target) is { } reference ? Resolve(reference, usages) : null;
        return new AiModelFeed(from, to, target, label);
    }

    /// <summary>The usage a reference addresses: a chapter of an <c>ai/</c> file
    /// — or of a bare file name, which is this folder — matched by file name and
    /// heading slug. The slug is matched on both rules in use: the app's own,
    /// which the chapter link carries, and GitHub's, which an author copies.</summary>
    private static AiModelUsage? Resolve(DevbookReference reference, IReadOnlyList<AiModelUsage> usages)
    {
        if (reference.Slug is null) return null;

        var inFolder = reference.Folder == DevbookFolder.Ai
            || (reference.Folder == DevbookFolder.Unknown && !reference.Path.Contains('/', StringComparison.Ordinal));
        if (!inFolder) return null;

        return usages.FirstOrDefault(usage =>
            string.Equals(usage.FileName, reference.FileName, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(usage.Link.Anchor, reference.Slug, StringComparison.OrdinalIgnoreCase)
                || string.Equals(GitHubSlug(usage.Heading), reference.Slug, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>GitHub's anchor rule, as the devbook generator spells it: lower-case,
    /// trim, keep letters, digits, underscores, whitespace and hyphens, and turn
    /// each whitespace character into a hyphen without collapsing runs.</summary>
    private static string GitHubSlug(string heading)
    {
        var kept = heading.Trim().ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || char.IsWhiteSpace(ch))
            .Select(ch => char.IsWhiteSpace(ch) ? '-' : ch);
        return new string([.. kept]);
    }

    internal static string? HighestRung(IReadOnlyList<AiModelUsage> usages) =>
        RungsHighestFirst.FirstOrDefault(rung =>
            usages.Any(usage => string.Equals(usage.Status, rung, StringComparison.OrdinalIgnoreCase)));
}

/// <param name="Key">The stage's word from the fixed vocabulary.</param>
/// <param name="Name">Its display name.</param>
/// <param name="BeforePullRequest">Whether the stage sits before the pull
/// request, in the author's session, rather than after it.</param>
/// <param name="Substages">The <c>substage</c> entries naming this stage, in
/// order of first appearance across the files.</param>
/// <param name="Usages">The chapters whose <c>stage</c> lists this one.</param>
/// <param name="Status">The highest rung among them — derived, authored nowhere —
/// or null for an empty stage.</param>
public sealed record AiModelStage(
    string Key,
    string Name,
    bool BeforePullRequest,
    IReadOnlyList<AiModelSubstage> Substages,
    IReadOnlyList<AiModelUsage> Usages,
    string? Status)
{
    public bool IsEmpty => Usages.Count == 0;
}

/// <param name="Key">The entry as written, <c>&lt;stage&gt;/&lt;word&gt;</c>.</param>
/// <param name="Word">The repository's own word after the slash.</param>
/// <param name="Usages">Every chapter naming the entry, in reading order.</param>
public sealed record AiModelSubstage(string Key, string Word, IReadOnlyList<AiModelUsage> Usages);

/// <param name="Key">The layer's word from the fixed vocabulary.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Usages">The chapters whose <c>layer</c> lists this one.</param>
/// <param name="Status">The highest rung among them, or null when empty.</param>
public sealed record AiModelLayer(string Key, string Name, IReadOnlyList<AiModelUsage> Usages, string? Status)
{
    public bool IsEmpty => Usages.Count == 0;
}

/// <summary>A <c>feeds</c> hand-off from one usage to another.</summary>
/// <param name="From">The usage whose block carries the entry.</param>
/// <param name="To">The usage the reference addresses, or null when it
/// addresses none.</param>
/// <param name="Target">The reference before the colon, as written.</param>
/// <param name="Label">The arrow's label, or null when the entry has none.</param>
public sealed record AiModelFeed(AiModelUsage From, AiModelUsage? To, string Target, string? Label)
{
    public bool IsResolved => To is not null && Label is not null;
}

/// <summary>A <c>related</c> link: one chapter supporting or steering another
/// without handing it work.</summary>
/// <param name="From">The usage whose block carries the link.</param>
/// <param name="To">The usage it addresses, or null when it points outside the
/// folder's usages — at a decision, a domain chapter, a concept file.</param>
/// <param name="Target">The reference as written.</param>
public sealed record AiModelSupport(AiModelUsage From, AiModelUsage? To, DevbookReference Target);

/// <summary>One AI usage: a chapter of a usage file, with every field the views
/// read from it.</summary>
/// <param name="Evidence">Its <c>evidence</c>: recorded, partial, fired or none.</param>
/// <param name="MovesUpWhen">The text of its <b>Moves up when</b> bullet.</param>
/// <param name="Stages">The stages its <c>stage</c> lists, on the vocabulary.</param>
/// <param name="Substages">Its <c>substage</c> entries whose stage it lists.</param>
/// <param name="Layers">The layers its <c>layer</c> lists, on the vocabulary.</param>
/// <param name="DependsOn">What its <c>depends-on</c> points at.</param>
/// <param name="Link">Where following the usage lands: its own chapter.</param>
public sealed record AiModelUsage(
    string Heading,
    string FileName,
    string? Type,
    string? Status,
    string? Evidence,
    string? MovesUpWhen,
    IReadOnlyList<string> Stages,
    IReadOnlyList<string> Substages,
    IReadOnlyList<string> Layers,
    IReadOnlyList<DevbookReference> DependsOn,
    string? Date,
    DevbookChapterLink Link)
{
    private const string MovesUpWhenLead = "Moves up when";

    /// <summary>What its <c>depends-on</c> points at, as short labels — never
    /// <c>related</c>, which is a reading hint and not a dependency.</summary>
    public IReadOnlyList<string> Technologies =>
        [.. DependsOn.Select(reference => reference.Label).Distinct(StringComparer.OrdinalIgnoreCase)];

    internal static AiModelUsage From(
        DocumentDevbookFile file,
        DocumentDevbookSection section,
        IReadOnlyList<string> stages,
        IReadOnlyList<string> substages,
        IReadOnlyList<string> layers) =>
        new(
            section.Heading,
            file.FileName,
            section.Meta.Type,
            section.Meta.Status,
            section.Meta.Extra.TryGetValue("evidence", out var evidence) && evidence.Count > 0 ? evidence[0].Trim().ToLowerInvariant() : null,
            MovesUpWhenOf(section),
            stages,
            substages,
            layers,
            section.Meta.DependsOn,
            section.Meta.Date,
            new DevbookChapterLink(
                DocumentDevbookFolder.Ai.AreaKey,
                DocumentDevbookFolder.Ai.DocumentPath(file.FileName),
                Slug(section.Heading)));

    /// <summary>The words after the bold <b>Moves up when</b> lead of a bullet
    /// in the chapter's body, without the dash or colon that joins them.</summary>
    private static string? MovesUpWhenOf(DocumentDevbookSection section)
    {
        foreach (var list in section.Blocks.OfType<DocumentDevbookList>())
        {
            foreach (var item in list.Items)
            {
                if (item.Count == 0 || item[0] is not MdStrong lead) continue;
                if (!string.Equals(lead.Text.Trim().TrimEnd(':', '.').Trim(), MovesUpWhenLead, StringComparison.OrdinalIgnoreCase)) continue;

                var text = string.Concat(item.Skip(1).Select(Plain)).Trim().TrimStart('—', '–', '-', ':').Trim();
                return text.Length == 0 ? null : text;
            }
        }

        return null;
    }

    private static string Plain(MdInline inline) => inline switch
    {
        MdText text => text.Text,
        MdStrong strong => strong.Text,
        MdEm em => em.Text,
        MdStrike strike => strike.Text,
        MdCodeSpan code => code.Text,
        MdLink link => link.Text,
        MdTag tag => "#" + tag.Tag,
        _ => string.Empty
    };

    /// <summary>The heading slug a chapter is addressed by — the same rule the
    /// status writer and the reference resolver use.</summary>
    private static string Slug(string heading)
    {
        var chars = heading.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
