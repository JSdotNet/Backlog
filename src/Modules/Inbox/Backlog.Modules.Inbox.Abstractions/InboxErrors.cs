using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Abstractions;

/// <summary>
/// Every <see cref="Error"/> an Inbox use case answers with, in one table so a
/// screen can match a code without reaching into a handler for it. Tasks keeps
/// its errors on the handlers that raise them; the Inbox has enough slices
/// answering the same "not found" that one list reads better than nine copies.
/// The messages are what the pane shows, so they are written for a person.
/// </summary>
public static class InboxErrors
{
    public static readonly Error ItemNotFound = Error.NotFound(
        "inbox.item.not_found",
        "That inbox item no longer exists.");

    public static readonly Error ItemNeedsTitle = Error.Validation(
        "inbox.item.needs_title",
        "A capture needs some text before it can be kept.");

    /// <summary>The aggregate refused a lifecycle step. Carries the aggregate's
    /// own words, which name the two states involved.</summary>
    public static Error InvalidTransition(string detail) => Error.Validation(
        "inbox.item.invalid_transition",
        detail);

    public static readonly Error AttachmentNotFound = Error.NotFound(
        "inbox.attachment.not_found",
        "That file is not on this item.");

    /// <summary>The file is named on the item but not on this machine yet —
    /// still waiting, or its fetch failed. The pane offers Retry for the second.</summary>
    public static readonly Error AttachmentNotDownloaded = Error.Validation(
        "inbox.attachment.not_downloaded",
        "That file has not been downloaded to this machine.");

    /// <summary>This head was composed without the attachment store or the
    /// sync source, so there is nowhere to fetch a file from or keep it in.</summary>
    public static readonly Error AttachmentsUnavailable = Error.Validation(
        "inbox.attachment.unavailable",
        "Attachments are not available here: this app has no sync service or no workspace folder to keep files in.");

    /// <summary>The item has been routed or archived, and its tags and
    /// repositories are what that decision was made with — changing them now
    /// would change nothing it produced. Said by the pane, which keeps such an
    /// item out of a tag or repository change across a selection.</summary>
    public static readonly Error ItemAlreadyDecided = Error.Validation(
        "inbox.item.already_decided",
        "Already routed or archived, so there is nothing left to change.");

    public static readonly Error PersonIsNotATag = Error.Validation(
        "inbox.tag.person_not_a_tag",
        "A person (@name) is a source, not a tag.");

    public static readonly Error ListNotFound = Error.NotFound(
        "inbox.list.not_found",
        "That list no longer exists.");

    public static readonly Error ListNeedsName = Error.Validation(
        "inbox.list.needs_name",
        "A list needs a name.");

    public static readonly Error ListDuplicateName = Error.Validation(
        "inbox.list.duplicate_name",
        "There is already a list with that name here.");

    public static readonly Error GroupNotFound = Error.NotFound(
        "inbox.group.not_found",
        "That group no longer exists.");

    public static readonly Error GroupNeedsName = Error.Validation(
        "inbox.group.needs_name",
        "A group needs a name.");

    public static readonly Error GroupDuplicateName = Error.Validation(
        "inbox.group.duplicate_name",
        "There is already a group with that name.");

    /// <summary>No drafter, or one that says it cannot run. The message is the
    /// drafter's own reason when it gave one, so Settings can be pointed at.</summary>
    public static Error PlanNotConfigured(string? reason) => Error.Validation(
        "inbox.plan.not_configured",
        reason ?? "No plan drafter is configured.");

    /// <summary>Transport, a non-2xx answer, or an empty one. A plain failure
    /// rather than a validation error: nothing the person typed caused it.</summary>
    public static Error PlanFailed(string detail) => new(
        "inbox.plan.failed",
        detail);

    public static readonly Error PlanEmpty = Error.Validation(
        "inbox.plan.empty",
        "The AI answer was not a plan.");

    /// <summary>The drafted plan named a repository the item is not assigned
    /// to. Named, so the person can see what the model made up — and assign the
    /// repository first if it was right.</summary>
    public static Error PlanUnknownRepository(string repoId) => Error.Validation(
        "inbox.plan.unknown_repository",
        $"The plan names a repository this item is not assigned to: {repoId}.");
}
