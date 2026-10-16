using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Abstractions.Services;

/// <summary>
/// The Devbook context, as the Inbox is allowed to see it: the person's Brain
/// (local ADR 0026), a place that takes a routed item as a new note and says
/// where it put it — the <c>ItemTriaged</c> knowledge route of
/// <c>.devbook/domain/devbook/dependencies.md</c>, realised as a call rather than
/// an event, as <see cref="IInboxBacklogTarget"/> realises the route to Tasks.
/// <para>
/// A port declared here and answered in <c>src/Infrastructure</c>, never a
/// reference to Devbook, for the reason the backlog target gives. A host may
/// leave it out — the mobile head has no Brain — and then To Brain answers
/// <see cref="InboxErrors.BrainUnavailableCode"/> and routes nothing.
/// </para>
/// </summary>
public interface IInboxBrainTarget
{
    /// <summary>Writes the item into the Brain's inbox area as a new note, keeping
    /// its title and body, and answers the note's path relative to the Brain's
    /// root. A Brain that is not set up, or cannot be written, fails with
    /// <see cref="InboxErrors.BrainUnavailableCode"/> and the reason in words, and
    /// nothing is written.</summary>
    Task<Result<string>> WriteNoteAsync(InboxBrainNoteRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Why a route To Brain would be refused right now — no Brain folder
    /// set, one that is missing, a host without a Brain — in the words the refusal
    /// would use; null while the Brain can take a note. Asked so triage can say so on
    /// the button and the suggestion chip before anybody presses them, rather than
    /// offering a decision it then refuses.</summary>
    string? UnavailableReason { get; }
}
