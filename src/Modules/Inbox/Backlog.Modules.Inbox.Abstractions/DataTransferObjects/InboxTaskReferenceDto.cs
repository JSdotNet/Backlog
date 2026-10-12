namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>
/// A backlog task as the Inbox may see it: only what an item's text could name
/// it by, and where it came from. The answer to
/// <see cref="Services.IInboxTaskReferences"/>, in the Inbox's own words — no
/// body, no entry grammar, and of its status only whether it is still open.
/// </summary>
/// <param name="Id">The task's id.</param>
/// <param name="ImportItemId">The <c>id:</c> it was imported under, when a plan
/// import made it — for an Inbox batch, the item's id — else null.</param>
/// <param name="Title">What the panel calls it.</param>
/// <param name="RepoIds">The repositories it targets, <c>owner/name</c>.</param>
/// <param name="Issues">The GitHub issues it was filed as.</param>
/// <param name="Links">The links it is known by: its issue and pull request
/// URLs, and the source link it was captured from.</param>
/// <param name="SourceInboxId">The Inbox item it was routed from, when it was
/// routed from one — what "Routed from this item" is read from. Last, with the
/// two after it, so every caller that names only what a dependency needs keeps
/// compiling.</param>
/// <param name="SourceUrl">The source link it was captured from — its
/// <c>Source:</c> line — or null. Also among <paramref name="Links"/>; here on
/// its own because "the same link" compares an item's source with a task's
/// source, not with every link the task is known by.</param>
/// <param name="IsOpen">Neither done nor archived. Always true in
/// <see cref="Services.IInboxTaskReferences.OpenTasksAsync"/>'s answer.</param>
/// <param name="Status">The task's status as a word (<c>draft</c>,
/// <c>ready</c>, <c>in-progress</c>, <c>done</c>), for the triage advisor to
/// read beside the title (local ADR 0023 §1); null from an adapter that does
/// not say.</param>
public sealed record InboxTaskReferenceDto(
    Guid Id,
    string? ImportItemId,
    string Title,
    IReadOnlyList<string> RepoIds,
    IReadOnlyList<InboxIssueReferenceDto> Issues,
    IReadOnlyList<string> Links,
    Guid? SourceInboxId = null,
    string? SourceUrl = null,
    bool IsOpen = true,
    string? Status = null);

/// <summary>A GitHub issue a task was filed as: its repository
/// (<c>owner/name</c>) and number.</summary>
public sealed record InboxIssueReferenceDto(string Repo, int Number);
