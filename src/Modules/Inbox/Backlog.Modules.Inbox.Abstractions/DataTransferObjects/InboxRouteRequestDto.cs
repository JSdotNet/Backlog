namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>
/// The published language of <c>ItemTriaged</c> when the route is Tasks
/// (<c>.devbook/domain/inbox/domain.md</c>): everything the Tasks context needs to make
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
/// <param name="AttachmentPath">The item's attachment folder when it arrived
/// with files, else null. Every entry made from the item points at it, so the
/// files go where the work goes.</param>
/// <param name="AfterItems">In a batch, the other items of the same batch this
/// one comes after — dependencies the person confirmed. Ids, not entry ids: which
/// of the target's entries the dependency names is known only once the adapter
/// has written the document, and a target left out of it is no dependency.
/// Null or empty on a single route.</param>
/// <param name="AfterTasks">In a batch, the tasks already in the backlog this
/// one comes after, each as the value its <c>after:</c> token is written with
/// (<see cref="DependencyTarget.After"/>). Null or empty on a single route.</param>
public sealed record InboxRouteRequestDto(
    Guid InboxItemId,
    string Title,
    string BodyMd,
    string? SourceUrl,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> RepoIds,
    string? AttachmentPath = null,
    IReadOnlyList<Guid>? AfterItems = null,
    IReadOnlyList<string>? AfterTasks = null);
