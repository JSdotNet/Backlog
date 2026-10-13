using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Features.RouteToBacklog;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.RouteToBrain;

/// <summary>Writes an item into the person's Brain as a new note, keeping its
/// title and body, and records that it went there — To Brain, <c>ItemTriaged</c>
/// with the route set to the Brain, realised as a call on
/// <see cref="IInboxBrainTarget"/> rather than an event.</summary>
public sealed record RouteToBrainCommand(Guid Id);

/// <summary>
/// The order of Route to Tasks, for its reason: the Brain is asked first and the
/// item is only marked routed once the note is written, so a Brain that is not
/// set up, or a folder that cannot be written, leaves the item exactly as it was
/// — open, in the queue, free to be routed once the Brain is there. The
/// aggregate's refusals are checked before the Brain is asked, so a second
/// routing writes no second note.
/// <para>
/// The target is optional: a host composed without a Brain answers
/// <see cref="InboxErrors.BrainUnavailableCode"/> rather than failing to build.
/// </para>
/// </summary>
public sealed class RouteToBrainCommandHandler(
    IInboxItemRepository items,
    TimeProvider clock,
    IInboxBrainTarget? target = null)
    : ICommandHandler<RouteToBrainCommand, Result<InboxBrainRoutedDto>>
{
    public async Task<Result<InboxBrainRoutedDto>> Handle(
        RouteToBrainCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return InboxErrors.ItemNotFound;

        if (RouteToBacklogCommandHandler.Refusal(item) is { } refused) return refused;
        if (target is null) return InboxErrors.BrainUnavailable(InboxErrors.NoBrainHere);

        var written = await target
            .WriteNoteAsync(new InboxBrainNoteRequestDto(item.Id, item.Title, item.BodyMd, item.SourceUrl), cancellationToken)
            .ConfigureAwait(false);
        if (written.IsFailure) return written.Error;

        item.RouteToBrain(clock.GetUtcNow());
        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return new InboxBrainRoutedDto(item.Id, written.Value);
    }
}
