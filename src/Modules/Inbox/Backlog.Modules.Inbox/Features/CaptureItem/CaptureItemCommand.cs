using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.CaptureItem;

/// <summary>
/// A thought typed straight into the desktop. The manual capture path — the
/// one that needs no paired device, which makes it both the offline path and
/// the way a QA run seeds an inbox without a phone.
/// </summary>
/// <param name="Channel">The capture source to file it under. <c>manual</c>
/// unless a desktop-side channel says otherwise; an item captured this way has
/// no replica behind it whatever the channel is called.</param>
public sealed record CaptureItemCommand(string Title, string Channel = InboxEnumMap.ManualChannel);

public sealed class CaptureItemCommandHandler(IInboxItemRepository items, TimeProvider clock)
    : ICommandHandler<CaptureItemCommand, Result<InboxItemDto>>
{
    public async Task<Result<InboxItemDto>> Handle(
        CaptureItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Title)) return InboxErrors.ItemNeedsTitle;

        var now = clock.GetUtcNow();

        // Captured and received at the same instant: there is no other machine
        // whose clock could have said something different.
        var item = InboxItem.Capture(
            command.Title,
            new InboxSource(InboxEnumMap.NormalizeChannel(command.Channel), Person: null),
            ContentKindDetector.FirstUrl(command.Title),
            ContentKindDetector.Detect(command.Title),
            capturedAt: now,
            receivedAt: now,
            replicaBacked: false);

        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return item.ToDto();
    }
}
