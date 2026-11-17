using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Abstractions.Services;

/// <summary>
/// The finished plans whose project note still sits in the Brain's projects area, and
/// the press that moves such a note to the archive (local ADR 0052 §5,
/// <c>.devbook/domain/roadmap/features.md#archiving-a-finished-plans-project-note</c>).
/// <para>
/// A plan is finished when every backlog entry it gathers is done
/// (<see cref="RoadmapItemRollupDto.IsFinished"/>). Its project notes are the resolved
/// Brain notes it gathers — by a <c>#brain:</c> Knowledge Ref or by its tag — that
/// <see cref="IRoadmapProjectNotes.CanArchive"/> answers for. The weekly review lists
/// them all; the app offers each plan's once, on this device.
/// </para>
/// </summary>
public interface IRoadmapProjectNoteArchive
{
    /// <summary>Every finished plan's project notes still in the projects area, by plan
    /// then by path. Empty in a host with no Brain, or a Brain with no archive area.</summary>
    Task<IReadOnlyList<FinishedProjectNote>> FinishedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Every finished plan, whether or not it gathers a project note, beside the project
    /// notes <see cref="FinishedAsync"/> answers — read in one gathering, so a watch
    /// can tell a plan that has just finished from one that finished before a Brain
    /// or its archive area was there to offer it.
    /// </summary>
    Task<RoadmapFinishedPlans> FinishedPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves <paramref name="note"/>'s file, unchanged, into the archive area, and points
    /// every Knowledge Ref of its plan that named the note at the new path, the heading
    /// it named kept. Answers the note's new path, or why nothing changed: a refused move
    /// leaves the plan as it was.
    /// </summary>
    Task<Result<string>> ArchiveAsync(FinishedProjectNote note, CancellationToken cancellationToken = default);

    /// <summary>Whether the plan has been offered the move on this device already.</summary>
    bool WasOffered(Guid itemId);

    /// <summary>Remembers on this device that the plan was offered the move, so it is
    /// never offered again — whatever the person did with the offer.</summary>
    void MarkOffered(Guid itemId);
}
