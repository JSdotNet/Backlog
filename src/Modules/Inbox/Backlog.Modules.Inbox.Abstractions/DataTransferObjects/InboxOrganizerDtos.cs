namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>A list an item can be filed in. <paramref name="GroupId"/> is null
/// for a list that sits at the top of the side menu on its own.</summary>
public sealed record InboxListDto(Guid Id, string Name, Guid? GroupId, int Order);

/// <summary>A fold in the side menu that holds lists.</summary>
public sealed record InboxGroupDto(Guid Id, string Name, int Order);

/// <summary>
/// Everything the pane draws, read in one go. Items are every status, archived
/// included — which slice a screen shows is the screen's decision, and a query
/// per slice would be three round trips to answer one render.
/// </summary>
public sealed record InboxSnapshotDto(
    IReadOnlyList<InboxItemDto> Items,
    IReadOnlyList<InboxListDto> Lists,
    IReadOnlyList<InboxGroupDto> Groups);
