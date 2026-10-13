namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>
/// The published language of <c>ItemTriaged</c> when the route is the Brain
/// (<c>.devbook/domain/inbox/domain.md#itemtriaged</c>, route <c>devbook</c>):
/// what Devbook needs to write an inbox item into the person's Brain as a note,
/// in the Inbox's own words. No note text here — how a title and a body become a
/// note with its <c>meta</c> block is Devbook's, and the adapter answering
/// <see cref="Services.IInboxBrainTarget"/> is what may know it.
/// </summary>
/// <param name="InboxItemId">The item being routed.</param>
/// <param name="Title">The item's title, one line.</param>
/// <param name="BodyMd">The item's body as captured, or the note as last
/// edited; empty for a bare one-line capture.</param>
/// <param name="SourceUrl">The link the capture came from, when it had one.</param>
public sealed record InboxBrainNoteRequestDto(
    Guid InboxItemId,
    string Title,
    string BodyMd,
    string? SourceUrl);

/// <summary>An item routed To Brain, and the note it became: the note's path
/// relative to the Brain's root (<c>_inbox/standup.md</c>).</summary>
public sealed record InboxBrainRoutedDto(Guid InboxItemId, string NotePath);
