namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>Why the Inbox thinks another item or a task has to do with the one
/// selected. Each is a fact it can check, never a guess from wording alone.</summary>
public enum InboxRelationKind
{
    /// <summary>The same source link, compared without case, fragment, trailing
    /// slash or <c>www.</c>.</summary>
    SameLink,

    /// <summary>The same site: the host, or on GitHub and GitLab the host and
    /// the repository.</summary>
    SameSite,

    /// <summary>A near-identical title — equal once normalised, or within one
    /// edit in ten.</summary>
    SimilarTitle,

    /// <summary>The task was made from this item: it carries the item's id as
    /// its source or its import id, or the item's routing names it.</summary>
    RoutedFromItem,

    /// <summary>The item's text names an issue the task was filed as.</summary>
    SameIssue
}

/// <summary>
/// What the selected item already has to do with the rest of the backlog: the
/// other Inbox items that look like the same capture, and the tasks that
/// already carry it. Read only — the Inbox never acts on a relation; the person
/// does, through "Archive as duplicate of…" or "Link to task…".
/// </summary>
/// <param name="ItemId">The item asked about.</param>
/// <param name="Items">Every other item that relates to it, strongest reason
/// first, then in the inbox's order.</param>
/// <param name="Tasks">Every task that relates to it, strongest reason first,
/// then in the backlog's order. Archived tasks are never among them.</param>
public sealed record InboxRelationsDto(
    Guid ItemId,
    IReadOnlyList<InboxRelatedItemDto> Items,
    IReadOnlyList<InboxRelatedTaskDto> Tasks)
{
    /// <summary>Nothing relates — also the answer for an item that is gone.</summary>
    public static InboxRelationsDto None(Guid itemId) => new(itemId, [], []);

    /// <summary>The backlog's open tasks, in its order: what "Link to task…"
    /// offers after the related ones. A member rather than a positional
    /// parameter because it is the picker's list, not a relation.</summary>
    public IReadOnlyList<InboxTaskOptionDto> OpenTasks { get; init; } = [];
}

/// <summary>Another Inbox item that relates to the selected one, and why.</summary>
/// <param name="Reason">The reason in words: "Same link", "Same site",
/// "Nearly the same title".</param>
public sealed record InboxRelatedItemDto(
    Guid Id,
    string Title,
    InboxStatus Status,
    InboxRelationKind Kind,
    string Reason);

/// <summary>A task that relates to the selected item, and why.</summary>
/// <param name="IsOpen">Whether the work is still to do: neither done nor
/// archived. The only status the Inbox sees of a task.</param>
/// <param name="Reason">The reason in words: "Same link", "Routed from this
/// item", "Links issue owner/name#12".</param>
public sealed record InboxRelatedTaskDto(
    Guid Id,
    string Title,
    bool IsOpen,
    InboxRelationKind Kind,
    string Reason);

/// <summary>A task "Link to task…" can offer: what it is called and nothing
/// else, because choosing it is all the picker does.</summary>
public sealed record InboxTaskOptionDto(Guid Id, string Title);
