using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Roadmap's <see cref="IRoadmapProjectNoteArchive"/> (local ADR 0052 §5): the
/// plan and its rollup are read through Roadmap's own ports, the projects and archive
/// areas through <see cref="IRoadmapProjectNotes"/>, which the Brain's adapter answers.
/// <para>
/// The move comes first and the plan second: a refused move leaves the plan's Knowledge
/// Refs as they were, and a moved note whose plan no longer names it — an edit made
/// meanwhile — is still moved, with nothing to repoint. The ref is rewritten through
/// <see cref="IRoadmapPlanning.UpdateItemAsync"/>, every other field sent back as it
/// was just read, so the item's window and its import placement are kept. The read and
/// the write are two calls, not one under the plan's gate: an edit to the same item
/// that lands between them is overwritten by the fields read before it.
/// </para>
/// <para>
/// Which plans were offered the move is kept per device in the shell's
/// <see cref="ShellNavigationStore"/>, beside the roadmap's other per-device choices;
/// a host that composed no such store holds it for the life of this instance.
/// </para>
/// </summary>
public sealed class RoadmapProjectNoteArchive : IRoadmapProjectNoteArchive
{
    private readonly IRoadmapPlanning _planning;
    private readonly IRoadmapItemRollup _rollup;
    private readonly IRoadmapProjectNotes? _notes;
    private readonly ShellNavigationStore? _store;
    private readonly HashSet<string> _offered = new(StringComparer.OrdinalIgnoreCase);

    public RoadmapProjectNoteArchive(
        IRoadmapPlanning planning,
        IRoadmapItemRollup rollup,
        IRoadmapProjectNotes? notes = null,
        ShellNavigationStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(planning);
        ArgumentNullException.ThrowIfNull(rollup);
        _planning = planning;
        _rollup = rollup;
        _notes = notes;
        _store = store;
    }

    public async Task<IReadOnlyList<FinishedProjectNote>> FinishedAsync(CancellationToken cancellationToken = default) =>
        _notes is null ? [] : (await FinishedPlansAsync(cancellationToken).ConfigureAwait(false)).Notes;

    public async Task<RoadmapFinishedPlans> FinishedPlansAsync(CancellationToken cancellationToken = default)
    {
        var plan = await _planning.GetPlanAsync(cancellationToken).ConfigureAwait(false);
        var rollups = await _rollup.GatherPlanAsync(plan, cancellationToken).ConfigureAwait(false);
        var finished = rollups.Where(rollup => rollup.Value.IsFinished).Select(rollup => rollup.Key).ToHashSet();
        var notes = _notes is null ? [] : FinishedProjectNote.Select(plan.Items, rollups, _notes.CanArchive);
        return new RoadmapFinishedPlans(finished, notes);
    }

    public async Task<Result<string>> ArchiveAsync(FinishedProjectNote note, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (_notes is null)
        {
            return Result.Failure<string>(Error.NotFound("roadmap.brain_unavailable", "No Brain is readable on this device."));
        }

        var moved = await _notes.ArchiveAsync(note.NotePath, cancellationToken).ConfigureAwait(false);
        if (!moved.Moved)
        {
            return Result.Failure<string>(Error.Validation("roadmap.archive_refused", moved.Error ?? "The note was not moved."));
        }

        var plan = await _planning.GetPlanAsync(cancellationToken).ConfigureAwait(false);
        var item = plan.Items.FirstOrDefault(candidate => candidate.Id == note.ItemId);
        var refs = item is null ? null : FinishedProjectNote.Repoint(item.Knowledge, note.NotePath, moved.NewPath!);
        if (item is null || refs is null) return Result.Success(moved.NewPath!);

        var updated = await _planning.UpdateItemAsync(
            item.Id,
            item.Title,
            item.Start,
            item.End,
            item.Priority,
            item.RepositoryAliases,
            item.Lane,
            item.TaskId,
            item.Notes,
            item.Tag,
            refs,
            cancellationToken).ConfigureAwait(false);

        return updated.IsSuccess
            ? Result.Success(moved.NewPath!)
            : Result.Failure<string>(Error.Validation(
                "roadmap.archive_ref_not_repointed",
                $"{note.NotePath} moved to {moved.NewPath}, but the plan still names the old path: {updated.Error.Message}"));
    }

    public bool WasOffered(Guid itemId)
    {
        var key = Key(itemId);
        return _store?.RoadmapArchiveOffered.Contains(key, StringComparer.OrdinalIgnoreCase) ?? _offered.Contains(key);
    }

    public void MarkOffered(Guid itemId)
    {
        var key = Key(itemId);
        if (_store is null)
        {
            _offered.Add(key);
            return;
        }

        _store.MarkRoadmapArchiveOffered(key);
    }

    private static string Key(Guid itemId) => itemId.ToString("D");
}
