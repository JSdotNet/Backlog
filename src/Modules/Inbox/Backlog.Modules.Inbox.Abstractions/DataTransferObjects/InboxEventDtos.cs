namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>One end of an edition as the capture wrote it: the day, and a time
/// with its UTC offset only when the source gave them
/// (<c>.devbook/domain/capture/domain.md#event-facts</c>).</summary>
public sealed record InboxEventDateDto(DateOnly Date, TimeOnly? Time = null, TimeSpan? Offset = null);

/// <summary>
/// The dates, place and watched page an event page capture found
/// (<c>.devbook/domain/inbox/domain.md#event-facts</c>), in the Inbox's words. Each
/// part is optional: a page with no date still arrives with its page.
/// </summary>
/// <param name="Start">When the edition starts, or null.</param>
/// <param name="End">When it ends, or null.</param>
/// <param name="Place">Where, as the page wrote it, or null.</param>
/// <param name="PageUrl">The watched page the edition was read from.</param>
/// <param name="PageTitle">That page's title.</param>
public sealed record InboxEventFactsDto(
    InboxEventDateDto? Start,
    InboxEventDateDto? End,
    string? Place,
    string? PageUrl,
    string? PageTitle);

/// <summary>
/// The published language of <c>ItemTriaged</c> when the route is Events
/// (<c>.devbook/domain/inbox/domain.md#itemtriaged</c>, route <c>events</c>): what
/// Events needs to make a Spotted knowledge event from an item.
/// </summary>
/// <param name="InboxItemId">The item being routed.</param>
/// <param name="Title">The item's title, one line.</param>
/// <param name="SourceUrl">The item's link, when it has one.</param>
/// <param name="Facts">What its capture found, or null for an item from any
/// source but an event page.</param>
public sealed record InboxEventRequestDto(
    Guid InboxItemId,
    string Title,
    string? SourceUrl,
    InboxEventFactsDto? Facts);

/// <summary>An item routed To Event, and the event it became.</summary>
public sealed record InboxEventRoutedDto(Guid InboxItemId, Guid EventId);
