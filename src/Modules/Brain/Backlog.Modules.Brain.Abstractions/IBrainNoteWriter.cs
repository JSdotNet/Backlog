namespace Backlog.Modules.Brain.Abstractions;

/// <summary>
/// Writes a new note into the Brain — its inbox area, or the area or project
/// folder the draft names
/// (<c>.devbook/domain/brain/features.md#capture-into-the-brain</c>): the one
/// writer the Devbook pane's <b>Capture</b>, Inbox triage's <b>To Brain</b> and an
/// assistant's capture tool all go through, so the three cannot write three kinds
/// of note. The text is <see cref="BrainNoteText.Compose"/>'s, and the area is
/// <see cref="BrainNoteText.InboxArea"/>'s unless the draft names a
/// <see cref="BrainNoteDraft.Folder"/>; <see cref="ListDestinationsAsync"/> lists
/// the ones it takes (<see cref="BrainNoteDestinations"/>).
/// <para>
/// It never overwrites: a title whose file name is taken gets the next free one.
/// A Brain that is not set up, a blank title, a destination that is not one of the
/// Brain's areas or a folder below one, or a folder that cannot be written is
/// answered as a refusal in words, never thrown — each is an ordinary thing for a
/// person to run into.
/// </para>
/// </summary>
public interface IBrainNoteWriter
{
    /// <summary>Writes <paramref name="draft"/> as a new note and answers where it
    /// went, or why nothing was written.</summary>
    Task<BrainNoteWrite> WriteAsync(BrainNoteDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Where a note can be written right now, in the order a picker lists
    /// them: the inbox area first, the Brain's other areas in tab order, and each
    /// note's folder in its <c>projects</c> area after that area
    /// (<see cref="BrainNoteDestinations.List"/>). Empty while the Brain is not set
    /// up. Never throws.</summary>
    Task<IReadOnlyList<BrainNoteDestination>> ListDestinationsAsync(CancellationToken cancellationToken = default);
}

/// <summary>A note to write: its title, its Markdown body (null or blank for
/// none), where it came from, the folder to write it into, relative to the
/// Brain's root — an area's folder or one below it (<c>someday</c>,
/// <c>projects/sync-rewrite</c>); null or blank for the inbox area — and the
/// Roadmap Tags its <c>roadmap</c> list starts with, for a project note a plan is
/// made beside (<see cref="BrainNoteText.Compose"/>); null or empty for none.</summary>
public sealed record BrainNoteDraft(
    string Title,
    string? BodyMd,
    BrainNoteSource Source,
    string? Folder = null,
    IReadOnlyList<string>? Roadmap = null);

/// <summary>
/// What a write did: the note's path relative to the Brain's root, with
/// <c>/</c> between segments (<c>_inbox/ideas-for-the-sync.md</c>), or the reason
/// nothing was written.
/// </summary>
public sealed record BrainNoteWrite
{
    private BrainNoteWrite(string? relativePath, string? area, string? error)
    {
        RelativePath = relativePath;
        Area = area;
        Error = error;
    }

    /// <summary>The note's path relative to the Brain's root; null when refused.</summary>
    public string? RelativePath { get; }

    /// <summary>The name of the area the note was written into (<c>Inbox</c>,
    /// <c>Projects</c>); null when refused, or when the writer did not say.</summary>
    public string? Area { get; }

    /// <summary>Why nothing was written; null when the note was.</summary>
    public string? Error { get; }

    /// <summary>Whether the note was written.</summary>
    public bool Written => Error is null;

    public static BrainNoteWrite To(string relativePath, string? area = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return new BrainNoteWrite(relativePath, area, null);
    }

    public static BrainNoteWrite Refused(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new BrainNoteWrite(null, null, error);
    }
}
