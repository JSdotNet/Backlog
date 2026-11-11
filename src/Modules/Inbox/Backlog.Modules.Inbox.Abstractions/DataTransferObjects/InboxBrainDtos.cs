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
/// <param name="IntoProjects">Write it into the Brain's projects area rather than
/// <paramref name="Folder"/> — the project note Create plan starts
/// (<c>.devbook/domain/inbox/features.md#create-plan-from-an-item</c>). A Brain
/// with no projects area refuses it.</param>
/// <param name="RoadmapTag">The Roadmap Tag the note's <c>roadmap</c> list starts
/// with — the plan's, for a project note — or null for none.</param>
public sealed record InboxBrainNoteRequestDto(
    Guid InboxItemId,
    string Title,
    string BodyMd,
    string? SourceUrl,
    string? Folder = null,
    bool IntoProjects = false,
    string? RoadmapTag = null);

/// <summary>The note a route To Brain wrote: its path relative to the Brain's
/// root, and the name of the area it went into.</summary>
public sealed record InboxBrainNoteWrittenDto(string NotePath, string Area);

/// <summary>An item routed To Brain, and the note it became: the note's path
/// relative to the Brain's root (<c>_inbox/standup.md</c>) and the name of the
/// area it is in (<c>Inbox</c>), which the toast names.</summary>
public sealed record InboxBrainRoutedDto(Guid InboxItemId, string NotePath, string Area = "Inbox");

/// <summary>The notes waiting in the Brain's inbox area — what the queue health
/// bar counts beside the Inbox's own items
/// (<c>.devbook/domain/inbox/features.md#queue-health-bar</c>).</summary>
/// <param name="AreaFolder">The inbox area's folder, relative to the Brain's root
/// (<c>_inbox</c>): what the Devbook is opened at.</param>
/// <param name="Count">How many notes sit in it, its sub-folders included.</param>
public sealed record InboxBrainUnfiledDto(string AreaFolder, int Count);

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
public sealed record InboxBrainDestinationDto(string Folder, string Label, bool IsInbox = false, bool IsProject = false)
{
    /// <summary>The folder of the Brain's <c>someday</c> area, where the Someday
    /// decision files an item (local ADR 0054 §3): To Brain with that area
    /// chosen, in one press. A Brain whose areas leave it out refuses the route
    /// in its own words.</summary>
    public const string SomedayFolder = "someday";
}
