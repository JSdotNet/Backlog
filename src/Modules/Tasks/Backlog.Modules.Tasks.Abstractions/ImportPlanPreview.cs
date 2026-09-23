namespace Backlog.Modules.Tasks.Abstractions;

/// <summary>
/// What a document brought in through Import will produce, read before anything is
/// written: how many roadmap items and how many tasks (ADR 0013, ruling 3).
/// <para>
/// The same split and the same plan-tag rule the import itself applies, so the
/// Import dialog can say what is about to happen without learning the grammar. A
/// count, not a promise — an item the roadmap already carries is revised rather
/// than created, and that is the roadmap's to find out.
/// </para>
/// </summary>
/// <param name="RoadmapItems">The distinct plan tags the document's <c>plan</c>
/// entries name — each entry with exactly one <c>+</c> tag — or, with "Lay out on the
/// roadmap" on and no <c>plan</c> entry written, the distinct <c>+</c> tags its task
/// entries carry.</param>
/// <param name="Tasks">The entries that are not <c>plan</c> entries.</param>
/// <param name="HasPlanEntries">Whether the document wrote a <c>plan</c> entry — the
/// dialog offers "Lay out on the roadmap" only when it did not.</param>
public sealed record ImportPlanPreview(int RoadmapItems, int Tasks, bool HasPlanEntries)
{
    /// <summary>Reads <paramref name="rawText"/> the way Import will.</summary>
    public static ImportPlanPreview Of(string? rawText, bool layOutOnRoadmap)
    {
        var parsed = EntryTextParser.SplitSegments(rawText ?? string.Empty)
            .Select(EntryTextParser.Parse)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Title))
            .ToList();

        var plans = parsed.Where(entry => entry.Kind == EntryKind.Plan).ToList();
        var tasks = parsed.Where(entry => entry.Kind != EntryKind.Plan).ToList();

        var items = plans.Count > 0
            ? plans.Select(entry => PlanTags(entry.Tags)).Where(tags => tags.Count == 1).Select(tags => tags[0])
            : layOutOnRoadmap ? tasks.SelectMany(entry => PlanTags(entry.Tags)) : [];

        return new ImportPlanPreview(
            items.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            tasks.Count,
            plans.Count > 0);
    }

    /// <summary>The <c>+</c> plan tags among an entry's tags, sigil kept, each
    /// once.</summary>
    public static IReadOnlyList<string> PlanTags(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        return [.. tags.Where(tag => tag.Length > 1 && tag[0] == '+').Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
