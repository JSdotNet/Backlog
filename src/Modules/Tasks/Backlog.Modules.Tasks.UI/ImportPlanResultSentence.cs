using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The sentence an import reports, both halves of it (ADR 0013, ruling 3).
/// <para>
/// The task half reads exactly as it always did, so an ordinary task-level import
/// says nothing new. The roadmap half follows only when something crossed, and it
/// says what the roadmap refused or could not place — a cycle leaves the tasks
/// written, and the reader has to learn that from here rather than from a roadmap
/// that simply did not change.
/// </para>
/// </summary>
public static class ImportPlanResultSentence
{
    public static string Describe(ImportPlanResultDto result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var parts = new List<string>
        {
            $"Imported: {result.Created} created, {result.Replaced} replaced, {result.Updated} updated, "
                + $"{result.Skipped} skipped, {result.Removed} removed."
        };

        if (result.Roadmap is { } roadmap)
        {
            parts.Add(roadmap.Refusal is { } refusal
                ? $"Roadmap: nothing laid out — {refusal.TrimEnd('.')}. The tasks were kept."
                : RoadmapCounts(roadmap));

            if (roadmap.Skipped.Count > 0)
            {
                parts.Add($"Not laid out (a plan entry needs exactly one + tag): {string.Join(", ", roadmap.Skipped)}.");
            }

            if (roadmap.EffortIgnored.Count > 0)
            {
                parts.Add($"effort: is not read on a plan entry — an item's size is what it gathers: {string.Join(", ", roadmap.EffortIgnored)}.");
            }

            if (roadmap.AmbiguousTags.Count > 0)
            {
                parts.Add($"More than one item carries {string.Join(", ", roadmap.AmbiguousTags.Select(tag => "+" + tag))}; the first was updated.");
            }
        }

        var unresolved = (result.UnresolvedTaskDependencies ?? [])
            .Concat(result.Roadmap?.UnresolvedDependencies ?? [])
            .ToList();
        if (unresolved.Count > 0)
        {
            parts.Add($"Unresolved after: {string.Join(", ", unresolved.Select(dependency => $"{dependency.Entry} → {dependency.After}"))}.");
        }

        return string.Join(" ", parts);
    }

    private static string RoadmapCounts(RoadmapIntakeResultDto roadmap)
    {
        var counts = $"Roadmap: {roadmap.Created} created, {roadmap.Updated} updated";
        if (roadmap.Relengthened > 0) counts += $", {roadmap.Relengthened} re-lengthened";
        return counts + ".";
    }
}
