using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.Abstractions;

/// <summary>
/// The Inbox and the Brain, as Events is allowed to use them when an event is harvested
/// (<c>.devbook/domain/events/domain.md#harvest-task-policy</c>, local ADR 0073 §8): a Talk note
/// leaves the Inbox for the Brain, so it has one home (ADR 0054). A port declared here and
/// answered in <c>src/Infrastructure</c> over the Inbox's and the Brain's published contracts,
/// never a reference to either.
/// <para>
/// Links run one way: nothing written through this port names the event. A host without a
/// Brain answers that every Talk note stays where it is.
/// </para>
/// </summary>
public interface IEventTalkNotes
{
    /// <summary>Writes the Inbox note <paramref name="noteId"/> as a new note in the Brain's inbox
    /// area, its title and body as the Inbox holds them, and answers the new note's path from
    /// the Brain's root and its title. Null when nothing was written: no Brain set, a Brain that
    /// refuses the note, or no such note in the Inbox. Never throws for any of those.</summary>
    Task<EventBrainNoteDto?> FileIntoBrainAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>Archives the Inbox note <paramref name="noteId"/>, once its words are in the
    /// Brain. A note already archived, routed or gone is refused, and nothing changes.</summary>
    Task<Result> ArchiveAsync(Guid noteId, CancellationToken cancellationToken = default);
}
