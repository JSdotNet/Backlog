namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>
/// An open backlog task as the Inbox may see it: only what an item's text could
/// name it by. The answer to <see cref="Services.IInboxTaskReferences"/>, in the
/// Inbox's own words — no status, no body, no entry grammar.
/// </summary>
/// <param name="Id">The task's id.</param>
/// <param name="ImportItemId">The <c>id:</c> it was imported under, when a plan
/// import made it — for an Inbox batch, the item's id — else null.</param>
/// <param name="Title">What the panel calls it.</param>
/// <param name="RepoIds">The repositories it targets, <c>owner/name</c>.</param>
/// <param name="Issues">The GitHub issues it was filed as.</param>
/// <param name="Links">The links it is known by: its issue and pull request
/// URLs, and the source link it was captured from.</param>
public sealed record InboxTaskReferenceDto(
    Guid Id,
    string? ImportItemId,
    string Title,
    IReadOnlyList<string> RepoIds,
    IReadOnlyList<InboxIssueReferenceDto> Issues,
    IReadOnlyList<string> Links);

/// <summary>A GitHub issue a task was filed as: its repository
/// (<c>owner/name</c>) and number.</summary>
public sealed record InboxIssueReferenceDto(string Repo, int Number);
