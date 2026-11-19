namespace Backlog.Modules.Events.Abstractions;

/// <summary>
/// The Brain, as Events is allowed to read it (<c>.devbook/domain/events/context.md</c>, local
/// ADR 0050 §3): which notes are at the paths an event links, with their titles, and which notes
/// the person can pick from. Read only; nothing is written to a note. A port declared here and
/// answered in <c>src/Infrastructure</c>; a host without a Brain answers that every note is
/// missing.
/// </summary>
public interface IEventBrainNotes
{
    /// <summary>Why no note can be linked right now — no Brain folder set, one that is missing,
    /// a host without a Brain — in words; null while the Brain can be read.</summary>
    string? UnavailableReason { get; }

    /// <summary>The notes there now among <paramref name="paths"/>, by path from the Brain's
    /// root. A path with no note behind it, or one outside the Brain, is left out.</summary>
    Task<IReadOnlyDictionary<string, EventBrainNoteDto>> ResolveAsync(
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default);

    /// <summary>The notes the picker offers, in title order. Empty without a Brain.</summary>
    Task<IReadOnlyList<EventBrainNoteDto>> CandidatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised when the Brain moved or was re-read; a reader asks again.</summary>
    event Action? Changed;
}
