namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>
/// The published language of <c>ItemTriaged</c> when the route is Tasks
/// (<c>.domain/inbox/domain.md</c>): everything the Tasks context needs to make
/// an entry out of an inbox item, in the Inbox's own words.
/// <para>
/// No entry text here. How a title, some tags and a repository become a
/// metadata line is Tasks' grammar (local ADR 0002), and the adapter that
/// answers <see cref="Services.IInboxBacklogTarget"/> is the thing allowed to
/// know it — the module hands over facts and never composes a line of markdown.
/// </para>
/// </summary>
/// <param name="InboxItemId">The item being routed; the adapter stamps it on
/// every entry it creates as the entry's <c>SourceInboxId</c>.</param>
/// <param name="Tags">Bare names, no <c>#</c>. Person tags are never here — the
/// aggregate refuses them as tags.</param>
/// <param name="RepoIds">Registry ids (<c>owner/name</c>), one entry each. Empty
/// means one entry with no repository.</param>
public sealed record InboxRouteRequestDto(
    Guid InboxItemId,
    string Title,
    string BodyMd,
    string? SourceUrl,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> RepoIds);
