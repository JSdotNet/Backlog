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
/// <param name="Folder">The destination the reader picked — a
/// <see cref="InboxBrainDestinationDto.Folder"/> — or null for the Brain's inbox
/// area.</param>
public sealed record InboxBrainNoteRequestDto(
    Guid InboxItemId,
    string Title,
    string BodyMd,
    string? SourceUrl,
    string? Folder = null);

/// <summary>The note a route To Brain wrote: its path relative to the Brain's
/// root, and the name of the area it went into.</summary>
public sealed record InboxBrainNoteWrittenDto(string NotePath, string Area);

/// <summary>An item routed To Brain, and the note it became: the note's path
/// relative to the Brain's root (<c>_inbox/standup.md</c>) and the name of the
/// area it is in (<c>Inbox</c>), which the toast names.</summary>
public sealed record InboxBrainRoutedDto(Guid InboxItemId, string NotePath, string Area = "Inbox");

/// <summary>
/// One place To Brain can file an item, as the destination picker lists it
/// (<c>.devbook/domain/inbox/features.md#route-to-brain</c>): an area of the
/// Brain, or a project's folder in its projects area.
/// </summary>
/// <param name="Folder">The folder, relative to the Brain's root, the note is
/// written into; handed back on the route.</param>
/// <param name="Label">What the picker shows.</param>
/// <param name="IsInbox">Whether it is the Brain's inbox area, which the picker
/// lists first and starts on.</param>
/// <param name="IsProject">Whether it is a project rather than an area.</param>
public sealed record InboxBrainDestinationDto(string Folder, string Label, bool IsInbox = false, bool IsProject = false);
