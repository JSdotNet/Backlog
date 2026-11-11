using Backlog.Modules.Brain.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Infrastructure.Brain;

/// <summary>
/// Answers Roadmap's <see cref="IRoadmapProjectNotes"/> over the Brain folder the
/// person picked (local ADR 0052 §5): the areas are the ones the Brain's conventions
/// declare, and the move is <see cref="IBrainNoteLinkWriter.ArchiveAsync"/>, which
/// moves the file without reading or changing a byte of it.
/// <para>
/// A project note is one whose first path segment is the projects area's folder — the
/// area named Projects, or the folder <c>projects</c> — and only while the Brain also
/// has an archive area, the same one the link writer moves into. A Brain without one is
/// never offered the move.
/// </para>
/// </summary>
public sealed class BrainRoadmapProjectNotes : IRoadmapProjectNotes
{
    private readonly BrainFolderSource _brain;
    private readonly IBrainNoteLinkWriter _writer;

    public BrainRoadmapProjectNotes(BrainFolderSource brain, IBrainNoteLinkWriter writer)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(writer);
        _brain = brain;
        _writer = writer;
    }

    public bool CanArchive(string notePath)
    {
        if (string.IsNullOrWhiteSpace(notePath)) return false;

        var segments = notePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return false;

        var areas = _brain.Areas();
        if (!areas.Any(BrainNoteLinkWriter.IsArchive)) return false;

        return areas.Any(area => IsProjects(area) && string.Equals(area.Folder, segments[0], StringComparison.OrdinalIgnoreCase));
    }

    public async Task<RoadmapNoteMove> ArchiveAsync(string notePath, CancellationToken cancellationToken = default)
    {
        var moved = await _writer.ArchiveAsync(notePath, cancellationToken).ConfigureAwait(false);
        return moved.Outcome is BrainLinkOutcome.Moved && moved.RelativePath is { } path
            ? RoadmapNoteMove.To(path)
            : RoadmapNoteMove.Refused(moved.Error ?? $"{notePath} was not moved.");
    }

    private static bool IsProjects(BrainArea area) =>
        string.Equals(area.Name.Trim(), "Projects", StringComparison.OrdinalIgnoreCase)
        || string.Equals(area.Folder, "projects", StringComparison.OrdinalIgnoreCase);
}
