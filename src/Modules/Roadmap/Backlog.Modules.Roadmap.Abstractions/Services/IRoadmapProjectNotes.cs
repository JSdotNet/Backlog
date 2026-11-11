namespace Backlog.Modules.Roadmap.Abstractions.Services;

/// <summary>
/// The Brain's projects and archive areas, as a finished plan reaches them (local ADR
/// 0052 §5): whether a note a plan gathers is a project note this device's Brain can
/// archive, and the move that archives it.
/// <para>
/// A port Roadmap owns and the Brain's adapter answers, as <see cref="IRoadmapBrainNotes"/>
/// is: this context never references the Brain's. The move itself is the Brain's link
/// writer's, which moves the file without changing a byte of it. A host with no Brain
/// composes none, and no plan is ever offered the move.
/// </para>
/// </summary>
public interface IRoadmapProjectNotes
{
    /// <summary>
    /// Whether <paramref name="notePath"/> — a path relative to the Brain's root, with
    /// <c>/</c> between segments — sits in the Brain's projects area, and the Brain has
    /// an archive area to move it into. False for every other note, and for a Brain
    /// with no archive area, which is never offered the move.
    /// </summary>
    bool CanArchive(string notePath);

    /// <summary>Moves the note's file, unchanged, into the archive area. Answers the
    /// note's new path, or why nothing moved; never throws for a refusal.</summary>
    Task<RoadmapNoteMove> ArchiveAsync(string notePath, CancellationToken cancellationToken = default);
}

/// <summary>What an archive move did: the note's new path, or why it did not move.</summary>
/// <param name="NewPath">The note's path relative to the Brain's root after the move;
/// null when it did not move.</param>
/// <param name="Error">Why it did not move; null when it did.</param>
public sealed record RoadmapNoteMove(string? NewPath, string? Error)
{
    /// <summary>Whether the note now sits in the archive area.</summary>
    public bool Moved => NewPath is not null;

    public static RoadmapNoteMove To(string newPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPath);
        return new RoadmapNoteMove(newPath, null);
    }

    public static RoadmapNoteMove Refused(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new RoadmapNoteMove(null, error);
    }
}
