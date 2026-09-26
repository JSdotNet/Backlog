using Backlog.UI.Components.Metadata;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The AI adoption record read as the DevOps loop — the picture
/// <c>devbook-ai.md</c> ("The loop picture") says the folder is authored for.
/// <para>
/// Every element is one field on a chapter and nothing is authored twice: the
/// eight stages are the fixed vocabulary, a usage sits at each stage its
/// <c>stage</c> lists, is shaded by its <c>status</c> and marked by its
/// <c>type</c>, and names the technology its <c>depends-on</c> points at. A stage
/// with no usage stays in the picture, empty — the emptiness is the finding.
/// </para>
/// </summary>
public sealed record AiLoop(IReadOnlyList<AiLoopStage> Stages, IReadOnlyList<AiLoopUsage> Concepts)
{
    /// <summary>The DevOps loop's own eight, in order: four on the dev half, four
    /// on the ops half, and <c>monitor</c> feeding <c>plan</c>.</summary>
    public static IReadOnlyList<string> StageKeys { get; } =
        ["plan", "code", "build", "test", "release", "deploy", "operate", "monitor"];

    /// <summary>The <c>.tech</c> ladder ranked by how far a usage has got, for a
    /// stage's own shade: adopted outranks trial outranks candidate, and a usage
    /// on hold or retired only shades a stage nothing live sits at.</summary>
    private static readonly string[] RungsHighestFirst = ["adopted", "trial", "candidate", "hold", "retired"];

    public static AiLoop From(IEnumerable<DocumentDevbookFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var byStage = StageKeys.ToDictionary(key => key, _ => new List<AiLoopUsage>(), StringComparer.OrdinalIgnoreCase);
        var concepts = new List<AiLoopUsage>();

        foreach (var file in files)
        {
            foreach (var section in file.Sections)
            {
                // A section with no block is prose — the root's own chapters — and
                // not a usage at all.
                if (section.Meta.IsEmpty) continue;

                var usage = AiLoopUsage.From(file, section);
                var stages = StagesOf(section.Meta);

                if (stages.Count == 0)
                {
                    if (string.Equals(section.Meta.Type, "concept", StringComparison.OrdinalIgnoreCase)) concepts.Add(usage);
                    continue;
                }

                // A value off the vocabulary places the chapter nowhere rather than
                // inventing a ninth stage; the validator is what reports the typo.
                foreach (var stage in stages.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (byStage.TryGetValue(stage, out var list)) list.Add(usage);
                }
            }
        }

        return new AiLoop(
            [.. StageKeys.Select((key, index) => new AiLoopStage(key, index < 4, byStage[key], HighestRung(byStage[key])))],
            concepts);
    }

    private static IReadOnlyList<string> StagesOf(MetadataRecord meta) =>
        meta.Extra.TryGetValue("stage", out var values)
            ? [.. values.Select(value => value.Trim()).Where(value => value.Length > 0)]
            : [];

    private static string? HighestRung(IReadOnlyList<AiLoopUsage> usages) =>
        RungsHighestFirst.FirstOrDefault(rung =>
            usages.Any(usage => string.Equals(usage.Status, rung, StringComparison.OrdinalIgnoreCase)));
}

/// <param name="Key">The stage's word from the fixed vocabulary.</param>
/// <param name="IsDev">Whether the stage sits on the dev half of the loop.</param>
/// <param name="Usages">The chapters whose <c>stage</c> lists this one.</param>
/// <param name="Status">The highest rung among them — derived, authored nowhere —
/// or null for an empty stage.</param>
public sealed record AiLoopStage(string Key, bool IsDev, IReadOnlyList<AiLoopUsage> Usages, string? Status)
{
    public bool IsEmpty => Usages.Count == 0;
}

/// <summary>One AI usage as the loop draws it: a chapter of a usage file.</summary>
/// <param name="Technologies">What its <c>depends-on</c> points at, as short
/// labels — never <c>related</c>, which is a reading hint and not a
/// dependency.</param>
/// <param name="Link">Where following the usage lands: its own chapter.</param>
public sealed record AiLoopUsage(
    string Heading,
    string? Status,
    string? Type,
    IReadOnlyList<string> Technologies,
    string? Date,
    DevbookChapterLink Link)
{
    internal static AiLoopUsage From(DocumentDevbookFile file, DocumentDevbookSection section) =>
        new(
            section.Heading,
            section.Meta.Status,
            section.Meta.Type,
            [.. section.Meta.DependsOn.Select(reference => reference.Label).Distinct(StringComparer.OrdinalIgnoreCase)],
            section.Meta.Date,
            new DevbookChapterLink(
                DocumentDevbookFolder.Ai.AreaKey,
                DocumentDevbookFolder.Ai.DocumentPath(file.FileName),
                Slug(section.Heading)));

    /// <summary>The heading slug a chapter is addressed by — the same rule the
    /// status writer and the reference resolver use.</summary>
    private static string Slug(string heading)
    {
        var chars = heading.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
