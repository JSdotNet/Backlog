namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// Writes a new note into the Brain's inbox area
/// (<c>.devbook/domain/devbook/features.md#capture-into-the-brain</c>): the one
/// writer the Devbook pane's <b>Capture</b>, Inbox triage's <b>To Brain</b> and an
/// assistant's capture tool all go through, so the three cannot write three kinds
/// of note. The text is <see cref="BrainNoteText.Compose"/>'s, and the area is
/// <see cref="BrainNoteText.InboxArea"/>'s.
/// <para>
/// It never overwrites: a title whose file name is taken gets the next free one.
/// A Brain that is not set up, a blank title, or a folder that cannot be written
/// is answered as a refusal in words, never thrown — each is an ordinary thing for
/// a person to run into.
/// </para>
/// </summary>
public interface IBrainNoteWriter
{
    /// <summary>Writes <paramref name="draft"/> as a new note and answers where it
    /// went, or why nothing was written.</summary>
    Task<BrainNoteWrite> WriteAsync(BrainNoteDraft draft, CancellationToken cancellationToken = default);
}

/// <summary>A note to write: its title, its Markdown body (null or blank for
/// none), and where it came from.</summary>
public sealed record BrainNoteDraft(string Title, string? BodyMd, BrainNoteSource Source);

/// <summary>
/// What a write did: the note's path relative to the Brain's root, with
/// <c>/</c> between segments (<c>_inbox/ideas-for-the-sync.md</c>), or the reason
/// nothing was written.
/// </summary>
public sealed record BrainNoteWrite
{
    private BrainNoteWrite(string? relativePath, string? error)
    {
        RelativePath = relativePath;
        Error = error;
    }

    /// <summary>The note's path relative to the Brain's root; null when refused.</summary>
    public string? RelativePath { get; }

    /// <summary>Why nothing was written; null when the note was.</summary>
    public string? Error { get; }

    /// <summary>Whether the note was written.</summary>
    public bool Written => Error is null;

    public static BrainNoteWrite To(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return new BrainNoteWrite(relativePath, null);
    }

    public static BrainNoteWrite Refused(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new BrainNoteWrite(null, error);
    }
}
