namespace Backlog.Modules.Brain.Abstractions;

/// <summary>
/// One place a new note can be written: a folder relative to the Brain's root,
/// with <c>/</c> between segments (<c>someday</c>, <c>projects/sync-rewrite</c>),
/// and what a picker calls it.
/// </summary>
/// <param name="Folder">The folder the note is written into, relative to the
/// Brain's root.</param>
/// <param name="Label">What a picker shows: the area's name, or for a project the
/// area's name and the project note's path under it.</param>
/// <param name="Area">The name of the area the folder is in.</param>
/// <param name="IsInbox">Whether this is the inbox area, where a note lands when
/// no destination is given.</param>
/// <param name="IsProject">Whether this is a project's folder beside its note
/// rather than an area.</param>
public sealed record BrainNoteDestination(string Folder, string Label, string Area, bool IsInbox = false, bool IsProject = false);

/// <summary>
/// Where a new note may go (local ADR 0054 §2,
/// <c>.devbook/domain/inbox/features.md#route-to-brain</c>): the inbox area, the
/// Brain's other areas in tab order, and the folder of each note in its
/// <c>projects</c> area, so a capture is filed once rather than written to the
/// inbox and filed again.
/// <para>
/// Pure: the caller lists the areas and the project notes; this orders them,
/// names them, and decides which folder a requested destination is.
/// </para>
/// </summary>
public static class BrainNoteDestinations
{
    /// <summary>The folder of the PARA set's projects area.</summary>
    public const string ProjectsFolder = "projects";

    /// <summary>What separates an area's name from a project in a label.</summary>
    public const string LabelSeparator = " › ";

    /// <summary>
    /// The destinations, in the order a picker lists them: <paramref name="inbox"/>
    /// first, then every other area of <paramref name="areas"/> in tab order, each
    /// project right after the projects area it sits in, by its path. A project is
    /// one note under that area (<paramref name="projectNotes"/>, paths relative to
    /// the Brain's root), filed into beside it (<see cref="ProjectFolderFor"/>), so
    /// two projects may share a folder — the area's own, for notes directly in it —
    /// and are still two rows, each named for its note.
    /// </summary>
    public static IReadOnlyList<BrainNoteDestination> List(
        IReadOnlyList<BrainArea> areas,
        BrainArea inbox,
        IEnumerable<string>? projectNotes = null)
    {
        ArgumentNullException.ThrowIfNull(areas);
        ArgumentNullException.ThrowIfNull(inbox);

        var destinations = new List<BrainNoteDestination> { new(inbox.Folder, inbox.Name, inbox.Name, IsInbox: true) };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { inbox.Folder };
        var projects = ProjectsArea(areas);
        var notes = (projectNotes ?? [])
            .Select(Normalize)
            .Where(path => path is not null)
            .Select(path => path!)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var area in areas)
        {
            if (!seen.Add(area.Folder)) continue;

            destinations.Add(new BrainNoteDestination(area.Folder, area.Name, area.Name));
            if (projects is null || !ReferenceEquals(area, projects)) continue;

            foreach (var note in notes)
            {
                if (ProjectFolderFor(area.Folder, note) is not { } folder) continue;

                var shown = note[(area.Folder.Length + 1)..];
                shown = shown.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? shown[..^3] : shown;
                destinations.Add(new BrainNoteDestination(folder, area.Name + LabelSeparator + shown, area.Name, IsProject: true));
            }
        }

        return destinations;
    }

    /// <summary>The Brain's projects area: the one whose folder is
    /// <c>projects</c>, or else the one named <c>Projects</c>; null when it has
    /// none.</summary>
    public static BrainArea? ProjectsArea(IReadOnlyList<BrainArea> areas)
    {
        ArgumentNullException.ThrowIfNull(areas);

        return areas.FirstOrDefault(area => string.Equals(area.Folder, ProjectsFolder, StringComparison.OrdinalIgnoreCase))
            ?? areas.FirstOrDefault(area => string.Equals(area.Name.Trim(), "Projects", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The folder a note filed into the project <paramref name="notePath"/> goes
    /// into: the project's folder, beside its note — the folder the note sits in,
    /// <c>projects</c> for <c>projects/sync-rewrite.md</c> and
    /// <c>projects/launch</c> for <c>projects/launch/brief.md</c>. Null for a path
    /// that is not a note under <paramref name="projectsFolder"/>.
    /// </summary>
    public static string? ProjectFolderFor(string projectsFolder, string notePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectsFolder);

        if (Normalize(notePath) is not { } path) return null;

        var segments = path.Split('/');
        if (segments.Length < 2
            || !string.Equals(segments[0], projectsFolder, StringComparison.OrdinalIgnoreCase)
            || !segments[^1].EndsWith(".md", StringComparison.Ordinal)
            || segments[^1].Length == 3)
        {
            return null;
        }

        return string.Join('/', segments[..^1]);
    }

    /// <summary>
    /// The folder a write asked for <paramref name="folder"/> lands in, or null
    /// when it may not: an area's folder of <paramref name="areas"/> or
    /// <paramref name="inbox"/>, spelt as the area spells it, or a folder below
    /// one. Each segment is one plain folder name: never empty, <c>.</c>,
    /// <c>..</c>, a dot-folder or rooted, so a destination never climbs out of the
    /// Brain or into a folder it does not index. <c>\</c> reads as <c>/</c>.
    /// </summary>
    public static string? Resolve(string? folder, IReadOnlyList<BrainArea> areas, BrainArea inbox)
    {
        ArgumentNullException.ThrowIfNull(areas);
        ArgumentNullException.ThrowIfNull(inbox);

        if (Normalize(folder) is not { } path) return null;

        var segments = path.Split('/');
        var area = (string.Equals(segments[0], inbox.Folder, StringComparison.OrdinalIgnoreCase) ? inbox : null)
            ?? areas.FirstOrDefault(candidate => string.Equals(candidate.Folder, segments[0], StringComparison.OrdinalIgnoreCase));
        if (area is null) return null;

        segments[0] = area.Folder;
        return string.Join('/', segments);
    }

    /// <summary>The area <paramref name="folder"/> is in, by its first segment;
    /// null when none of <paramref name="areas"/> or <paramref name="inbox"/> is.</summary>
    public static BrainArea? AreaOf(string folder, IReadOnlyList<BrainArea> areas, BrainArea inbox)
    {
        ArgumentNullException.ThrowIfNull(areas);
        ArgumentNullException.ThrowIfNull(inbox);

        var top = (folder ?? string.Empty).Split('/')[0];
        return string.Equals(top, inbox.Folder, StringComparison.OrdinalIgnoreCase)
            ? inbox
            : areas.FirstOrDefault(area => string.Equals(area.Folder, top, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A relative path with <c>/</c> between plain segments, or null.</summary>
    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var trimmed = path.Trim().Replace('\\', '/');
        if (trimmed.StartsWith('/') || trimmed.Contains(':', StringComparison.Ordinal)) return null;

        var segments = trimmed.TrimEnd('/').Split('/');
        foreach (var segment in segments)
        {
            if (segment.Length == 0
                || segment.StartsWith('.')
                || segment.Trim().Length != segment.Length
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || segment.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0)
            {
                return null;
            }
        }

        return string.Join('/', segments);
    }
}
