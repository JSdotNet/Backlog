namespace Backlog.Modules.Brain.Abstractions;

/// <summary>
/// Writes a link into a note already in the Brain, and moves a finished project
/// note into the archive area (local ADR 0052): the port
/// <c>.devbook/domain/brain/domain.md#cross-linking</c> writes through, beside
/// <see cref="IBrainNoteWriter"/>, which only ever creates notes.
/// <para>
/// A link is an append to one of two lists in the note's top <c>meta</c> block —
/// a task id to <c>tasks</c>, a Roadmap Tag to <c>roadmap</c> — and the text edit
/// itself is <see cref="BrainNoteLinks.Append"/>'s, which keeps every other byte
/// of the file. A note is named by its path relative to the Brain's root, with
/// <c>/</c> between segments, as <see cref="BrainNoteWrite.RelativePath"/> names
/// one. A Brain that is not set up, a note that is not there, or a block that does
/// not parse is answered as a refusal in words, never thrown.
/// </para>
/// </summary>
public interface IBrainNoteLinkWriter
{
    /// <summary>Appends <paramref name="taskId"/> to the note's <c>tasks</c> list.</summary>
    Task<BrainLinkWrite> AppendTaskAsync(string notePath, string taskId, CancellationToken cancellationToken = default);

    /// <summary>Appends <paramref name="roadmapTag"/> to the note's <c>roadmap</c> list.</summary>
    Task<BrainLinkWrite> AppendRoadmapTagAsync(string notePath, string roadmapTag, CancellationToken cancellationToken = default);

    /// <summary>Moves the note's file, unchanged, into the Brain's archive area,
    /// keeping its path below the area it leaves.</summary>
    Task<BrainLinkWrite> ArchiveAsync(string notePath, CancellationToken cancellationToken = default);
}

/// <summary>What a link write or an archive move did.</summary>
public enum BrainLinkOutcome
{
    /// <summary>The value was appended and the note written.</summary>
    Appended,

    /// <summary>The list already held the value; the note was not written.</summary>
    AlreadyPresent,

    /// <summary>The note was moved into the archive area.</summary>
    Moved,

    /// <summary>Nothing was written; <see cref="BrainLinkWrite.Error"/> says why.</summary>
    Refused,
}

/// <summary>
/// What a link write or an archive move did: the note's path relative to the
/// Brain's root afterwards — its new one after a move — or the reason nothing was
/// written.
/// </summary>
public sealed record BrainLinkWrite
{
    private BrainLinkWrite(BrainLinkOutcome outcome, string? relativePath, string? error)
    {
        Outcome = outcome;
        RelativePath = relativePath;
        Error = error;
    }

    /// <summary>What happened.</summary>
    public BrainLinkOutcome Outcome { get; }

    /// <summary>The note's path relative to the Brain's root; null when refused.</summary>
    public string? RelativePath { get; }

    /// <summary>Why nothing was written; null otherwise.</summary>
    public string? Error { get; }

    /// <summary>Whether the note now carries the link, or sits in the archive area.</summary>
    public bool Succeeded => Outcome != BrainLinkOutcome.Refused;

    public static BrainLinkWrite Appended(string relativePath) => new(BrainLinkOutcome.Appended, Required(relativePath), null);

    public static BrainLinkWrite AlreadyPresent(string relativePath) => new(BrainLinkOutcome.AlreadyPresent, Required(relativePath), null);

    public static BrainLinkWrite Moved(string relativePath) => new(BrainLinkOutcome.Moved, Required(relativePath), null);

    public static BrainLinkWrite Refused(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new BrainLinkWrite(BrainLinkOutcome.Refused, null, error);
    }

    private static string Required(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return relativePath;
    }
}
