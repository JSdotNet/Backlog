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
/// <param name="Notes">What was written beneath the title — the Add dialog's
/// notes field — kept as the item's body. Null or blank means a bare one-line
/// capture, which is what the older Enter-to-capture field produced.</param>
/// <param name="Channel">The capture source to file it under. <c>manual</c>
/// unless a desktop-side channel says otherwise; an item captured this way has
/// no replica behind it whatever the channel is called.</param>
/// <param name="SourceUrl">The link the capture is about, when the caller names
/// one beside the title — an assistant's <c>capture_to_inbox</c> does. Only an
/// absolute http or https address; null or blank reads the link off the title,
/// as the capture field always has.</param>
public sealed record CaptureItemCommand(
    string Title,
    string? Notes = null,
    string Channel = InboxEnumMap.ManualChannel,
    string? SourceUrl = null);

public sealed class CaptureItemCommandHandler(IInboxItemRepository items, TimeProvider clock)
    : ICommandHandler<CaptureItemCommand, Result<InboxItemDto>>
{
    public async Task<Result<InboxItemDto>> Handle(
        CaptureItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Title)) return InboxErrors.ItemNeedsTitle;

        var named = string.IsNullOrWhiteSpace(command.SourceUrl) ? null : command.SourceUrl.Trim();
        if (named is not null && !IsWebAddress(named)) return InboxErrors.SourceUrlNotALink;

        var now = clock.GetUtcNow();

        // Captured and received at the same instant: there is no other machine
        // whose clock could have said something different.
        var item = InboxItem.Capture(
            command.Title,
            new InboxSource(InboxEnumMap.NormalizeChannel(command.Channel), Person: null),
            named ?? ContentKindDetector.FirstUrl(command.Title),
            ContentKindDetector.Detect(command.Title, named, bodyMd: null),
            capturedAt: now,
            receivedAt: now,
            replicaBacked: false,
            bodyMd: string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim());

        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return item.ToDto();
    }

    /// <summary>An absolute http or https address: what the detail opens as a
    /// link, so nothing else — a relative path, a <c>file:</c> or
    /// <c>javascript:</c> address — is kept as one.</summary>
    private static bool IsWebAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
