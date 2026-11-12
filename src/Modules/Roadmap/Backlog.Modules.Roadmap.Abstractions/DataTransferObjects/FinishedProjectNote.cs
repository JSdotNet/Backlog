using Backlog.SharedKernel;

namespace Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

/// <summary>Every finished plan, and the project notes among them still to archive.</summary>
/// <param name="Plans">The roadmap items whose gathered backlog entries are all done.</param>
/// <param name="Notes">Their project notes still in the projects area, by plan then by path.</param>
public sealed record RoadmapFinishedPlans(IReadOnlySet<Guid> Plans, IReadOnlyList<FinishedProjectNote> Notes)
{
    /// <summary>No plan is finished.</summary>
    public static RoadmapFinishedPlans None { get; } = new(new HashSet<Guid>(), []);
}

/// <summary>
/// A finished plan's project note, still in the Brain's projects area (local ADR 0052 §5).
/// </summary>
/// <param name="ItemId">The plan's roadmap item.</param>
/// <param name="PlanTitle">The plan's title.</param>
/// <param name="NotePath">The note's path relative to the Brain's root.</param>
/// <param name="NoteTitle">The note's title.</param>
public sealed record FinishedProjectNote(Guid ItemId, string PlanTitle, string NotePath, string NoteTitle)
{
    /// <summary>
    /// The finished plans' project notes among <paramref name="items"/>, by plan then by
    /// path: each resolved Brain note a finished plan gathers that
    /// <paramref name="canArchive"/> answers for, once per plan however many headings of
    /// it the plan names.
    /// </summary>
    public static IReadOnlyList<FinishedProjectNote> Select(
        IEnumerable<RoadmapItemDto> items,
        IReadOnlyDictionary<Guid, RoadmapItemRollupDto> rollups,
        Func<string, bool> canArchive)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(rollups);
        ArgumentNullException.ThrowIfNull(canArchive);

        var found = new List<FinishedProjectNote>();
        foreach (var item in items)
        {
            if (!rollups.TryGetValue(item.Id, out var rollup) || !rollup.IsFinished) continue;

            var notes = rollup.BrainNotes
                .Where(link => !link.Unresolved)
                .SelectMany(link => BrainNoteAddress.TryParse(link.Key, out var address)
                    ? [(address.Path, link.Title)]
                    : Array.Empty<(string Path, string Title)>())
                .Where(note => canArchive(note.Path))
                .DistinctBy(note => note.Path, StringComparer.OrdinalIgnoreCase)
                .OrderBy(note => note.Path, StringComparer.OrdinalIgnoreCase)
                .Select(note => new FinishedProjectNote(item.Id, item.Title, note.Path, note.Title));

            found.AddRange(notes);
        }

        return found;
    }

    /// <summary>
    /// <paramref name="references"/> with every <c>#brain:</c> ref that names
    /// <paramref name="oldPath"/> pointed at <paramref name="newPath"/>, the heading it
    /// named kept, and every other ref as it was written. Null when none named the note.
    /// </summary>
    public static IReadOnlyList<string>? Repoint(IReadOnlyList<string> references, string oldPath, string newPath)
    {
        ArgumentNullException.ThrowIfNull(references);

        var changed = false;
        var repointed = references
            .Select(reference =>
            {
                if (!BrainNoteAddress.TryParse(reference, out var address)
                    || !string.Equals(address.Path, oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    return reference;
                }

                changed = true;
                return BrainNoteAddress.Format(newPath, address.Slug);
            })
            .ToList();

        return changed ? repointed : null;
    }
}
