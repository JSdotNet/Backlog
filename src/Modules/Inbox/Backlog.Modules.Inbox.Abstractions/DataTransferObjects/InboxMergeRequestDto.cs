namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>
/// What "Merge into a task" hands the Tasks context: the capture being folded
/// into a task it repeats, in the Inbox's own words, and the task it goes into.
/// <para>
/// Facts, never a comment's text. How the title, the link and the notes are
/// laid out on the task is the adapter answering
/// <see cref="Services.IInboxBacklogTarget"/>'s to decide — the same division
/// <see cref="InboxRouteRequestDto"/> keeps for a new entry.
/// </para>
/// </summary>
/// <param name="InboxItemId">The capture being merged.</param>
/// <param name="TaskId">The backlog task it is merged into.</param>
/// <param name="Title">The capture's title.</param>
/// <param name="SourceUrl">The capture's link, or null when it has none.</param>
/// <param name="BodyMd">The capture's notes; empty when it has none.</param>
public sealed record InboxMergeRequestDto(
    Guid InboxItemId,
    Guid TaskId,
    string Title,
    string? SourceUrl,
    string BodyMd);
