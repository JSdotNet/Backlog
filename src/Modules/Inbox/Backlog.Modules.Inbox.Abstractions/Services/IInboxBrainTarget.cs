using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Abstractions.Services;

/// <summary>
/// The Brain context, as the Inbox is allowed to see it: the person's Brain
/// (local ADR 0026, ADR 0040), a place that takes a routed item as a new note and
/// says where it put it — the <c>ItemTriaged</c> knowledge route of
/// <c>.devbook/domain/brain/context.md#dependencies</c>, realised as a call rather than
/// an event, as <see cref="IInboxBacklogTarget"/> realises the route to Tasks.
/// <para>
/// A port declared here and answered in <c>src/Infrastructure</c>, never a
/// reference to the Brain, for the reason the backlog target gives. A host may
/// leave it out — the mobile head has no Brain — and then To Brain answers
/// <see cref="InboxErrors.BrainUnavailableCode"/> and routes nothing.
/// </para>
/// </summary>
public interface IInboxBrainTarget
{
    /// <summary>Writes the item into the Brain as a new note, keeping its title
    /// and body — into the destination the request names, else the inbox area — and
    /// answers the note's path relative to the Brain's root and its area's name. A
    /// Brain that is not set up, a destination it does not have, or a folder that
    /// cannot be written fails with <see cref="InboxErrors.BrainUnavailableCode"/>
    /// and the reason in words, and nothing is written.</summary>
    Task<Result<InboxBrainNoteWrittenDto>> WriteNoteAsync(InboxBrainNoteRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Where To Brain can file an item right now, in the order the
    /// destination picker lists them: the inbox area first, the Brain's other
    /// areas in tab order, its projects after the projects area. Empty while the
    /// Brain cannot take a note.</summary>
    Task<IReadOnlyList<InboxBrainDestinationDto>> ListDestinationsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InboxBrainDestinationDto>>([]);

    /// <summary>Why a route To Brain would be refused right now — no Brain folder
    /// set, one that is missing, a host without a Brain — in the words the refusal
    /// would use; null while the Brain can take a note. Asked so triage can say so on
    /// the button and the suggestion chip before anybody presses them, rather than
    /// offering a decision it then refuses.</summary>
    string? UnavailableReason { get; }

    /// <summary>The notes waiting in the Brain's inbox area — the area a routed item
    /// lands in — and that area's folder; null while no Brain is set, its folder is
    /// missing, or the host has none, so the Inbox shows nothing for it. An area whose
    /// folder does not exist yet holds no note.</summary>
    Task<InboxBrainUnfiledDto?> UnfiledNotesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<InboxBrainUnfiledDto?>(null);
}
