using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.RouteToBacklog;

/// <summary>Turns an item into backlog entries — one per assigned repository, or
/// one untargeted entry — and records where they went. <c>ItemTriaged</c> with
/// the route set to Tasks, realised as a call on
/// <see cref="IInboxBacklogTarget"/> rather than an event.
/// <paramref name="EntryType"/> is the kind of entry to make, one of
/// <see cref="InboxTriagePassDto.EntryTypes"/> compared case-insensitively, or
/// null for a task.</summary>
public sealed record RouteToBacklogCommand(Guid Id, string? EntryType = null);

/// <summary>
/// The order of the two writes is the decision here. Tasks is asked first and
/// the item is only marked routed once Tasks has answered with ids, so a
/// failure on the far side leaves the item exactly as it was — unrouted, still
/// in the queue, and free to be tried again. The reverse order would leave an
/// item claiming entries that were never made. The cost is the opposite gap: a
/// crash between the two writes leaves entries with a source id and an item that
/// does not know about them, which the person can see in the backlog and which
/// routing again would only duplicate. The aggregate refuses a second routing,
/// so that case surfaces as an error rather than a second set of entries.
/// <para>
/// An item that arrived with files hands its folder to every entry it becomes,
/// as the entry's attachment, so the file goes where the work goes. The folder,
/// not the files: a task points at one place (Tasks' <c>Attachment</c>), and the
/// item's folder holds exactly its files. Handed on whether or not every file
/// arrived — the folder is still where they belong, and a Retry on the item
/// fills it in.
/// </para>
/// <para>
/// The captures archived as duplicates of the item, at any distance down the
/// chain — "keep one, attach the other" — go with it as facts, title and link, and the adapter writes them on
/// the entry. Read at route time rather than copied onto the kept item when the
/// duplicate was archived: only a note changes after it is captured
/// (<c>domain.md#content-kind</c>), and the restore that undoes the archive
/// clears the link, so an undone attach is simply no longer carried.
/// </para>
/// </summary>
public sealed class RouteToBacklogCommandHandler(
    IInboxItemRepository items,
    IInboxBacklogTarget target,
    TimeProvider clock,
    IInboxAttachmentFiles? attachmentFiles = null)
    : ICommandHandler<RouteToBacklogCommand, Result<InboxRoutedDto>>
{
    public async Task<Result<InboxRoutedDto>> Handle(
        RouteToBacklogCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The type goes into entry text as a word, so only the vocabulary's own
        // words get that far.
        var entryType = InboxTriagePassDto.EntryTypeOf(command.EntryType);
        if (entryType is null && !string.IsNullOrWhiteSpace(command.EntryType)) return InboxErrors.UnknownEntryType(command.EntryType.Trim());

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return InboxErrors.ItemNotFound;

        // Refused before Tasks is asked, so an item that cannot take the
        // routing costs no entries. The same guard the aggregate applies below;
        // stated here because "route twice" is the one mistake a double-click
        // makes and it should make no entry.
        if (Refusal(item) is { } refused) return refused;

        var request = RequestFor(item, attachmentFiles) with
        {
            EntryType = entryType,
            Attached = await AttachedAsync(item, cancellationToken).ConfigureAwait(false),
        };

        var created = await target.CreateTasksAsync(request, cancellationToken).ConfigureAwait(false);
        if (created.IsFailure) return created.Error;

        item.RouteToBacklog(created.Value, [.. item.RepoIds], clock.GetUtcNow());

        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return new InboxRoutedDto(item.Id, created.Value);
    }

    private async Task<IReadOnlyList<InboxAttachedDuplicateDto>> AttachedAsync(InboxItem kept, CancellationToken cancellationToken) =>
        AttachedTo(kept.Id, await items.ListAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// The captures archived as duplicates of <paramref name="keptId"/>, at any
    /// distance, oldest capture first. Archiving names the root only for the
    /// item archived; a kept item archived later as another's duplicate keeps
    /// its own duplicates pointing at it, so the links are walked down from the
    /// root — every archived item whose chain reaches it. A seen-set stops a
    /// loop only a hand-edited store could hold, as the archive's own root walk
    /// does. A link to a task is not a duplicate of an item and is not followed.
    /// Shared with the batch route, so the kept item carries them whichever way
    /// it leaves.
    /// </summary>
    internal static IReadOnlyList<InboxAttachedDuplicateDto> AttachedTo(Guid keptId, IReadOnlyList<InboxItem> all)
    {
        var children = all
            .Where(item => item.DuplicateOf is not null && !item.DuplicateOfTask && item.Status is InboxStatus.Archived)
            .ToLookup(item => item.DuplicateOf!.Value);

        var seen = new HashSet<Guid> { keptId };
        var found = new List<InboxItem>();
        var pending = new Queue<Guid>([keptId]);

        while (pending.TryDequeue(out var parent))
        {
            foreach (var child in children[parent])
            {
                if (!seen.Add(child.Id)) continue;

                found.Add(child);
                pending.Enqueue(child.Id);
            }
        }

        return [.. found
            .OrderBy(item => item.CapturedAt)
            .ThenBy(item => item.Id)
            .Select(item => new InboxAttachedDuplicateDto(item.Title, item.SourceUrl))];
    }

    /// <summary>Why the item cannot be routed — already routed, or archived —
    /// or null when it can. Shared with the batch route, so a selection refuses
    /// an item for exactly the reasons a single route does.</summary>
    internal static Error? Refusal(InboxItem item)
    {
        if (item.IsRouted) return InboxErrors.InvalidTransition(
            new InvalidInboxTransitionException(item.Status, "routed again").Message);
        if (item.Status is InboxStatus.Archived) return InboxErrors.InvalidTransition(
            new InvalidInboxTransitionException(item.Status, "routed").Message);

        return null;
    }

    /// <summary>The item's facts as Tasks is handed them, attachment folder
    /// included. Shared with the batch route, so an item routed in a batch
    /// becomes exactly the entries it would have become on its own.</summary>
    internal static InboxRouteRequestDto RequestFor(InboxItem item, IInboxAttachmentFiles? attachmentFiles) =>
        new(
            item.Id,
            item.Title,
            item.BodyMd,
            item.SourceUrl,
            [.. item.Tags.Select(tag => tag.Name)],
            [.. item.RepoIds],
            AttachmentFolder(item, attachmentFiles));

    /// <summary>The folder an item hands on as its tasks' attachment, or null
    /// when it arrived with no files or the host keeps none. Shared with Create
    /// plan, so the files go where the work goes whichever door it leaves by.</summary>
    internal static string? AttachmentFolder(InboxItem item, IInboxAttachmentFiles? attachmentFiles) =>
        item.Attachments.Count > 0 ? attachmentFiles?.FolderFor(item.Id) : null;
}
