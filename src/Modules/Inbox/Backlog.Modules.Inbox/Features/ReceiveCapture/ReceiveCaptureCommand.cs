using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.ReceiveCapture;

/// <summary>A capture that arrived with an id of its own — pulled from the
/// replica, live or tombstoned, or read off a feed by a source monitor —
/// offered to the inbox.</summary>
public sealed record ReceiveCaptureCommand(InboxCaptureDto Capture);

/// <summary>
/// Decides what an arriving capture means to this machine, by id and by
/// status. Written for the replica's captures; a feed's take the same path
/// with the same answers, and the replica-only cases simply never arise for
/// them because a feed sends no tombstones.
/// <para>
/// The four outcomes are the four combinations of "known here" and "withdrawn
/// there". An unknown live capture becomes an item with the capture's own id.
/// A known one arriving live again is a replay or an echo and changes nothing.
/// A tombstone for an item still open here — unprocessed or deferred — is the
/// phone saying it dismissed the thought, or another desktop saying it dealt
/// with it, and the item is archived to match; <em>without</em> the outbox
/// flag, because the replica already holds the tombstone this desktop would
/// otherwise push. A tombstone for an item this desktop already routed or
/// archived is its own acknowledgement coming back round, and a tombstone for
/// an id it has never seen is nothing to act on.
/// </para>
/// <para>
/// Never a failed <see cref="Result"/>, and never a throw. The caller is the
/// sync client, and an error here would be an error it cannot correct; every
/// case has a right answer, so every case answers. That includes a live
/// capture with no title — the push endpoint validates nothing — which is
/// ignored rather than handed to the aggregate to refuse.
/// </para>
/// </summary>
public sealed class ReceiveCaptureCommandHandler(IInboxItemRepository items, TimeProvider clock)
    : ICommandHandler<ReceiveCaptureCommand, Result<InboxIntakeOutcome>>
{
    public async Task<Result<InboxIntakeOutcome>> Handle(
        ReceiveCaptureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var capture = command.Capture;
        var existing = await items.GetAsync(capture.Id, cancellationToken).ConfigureAwait(false);
        var withdrawn = capture.WithdrawnAt is not null;

        if (existing is null)
        {
            // Nothing to withdraw, or nothing to keep: a title is the one
            // thing an item cannot be made without, and the aggregate says so
            // by throwing — which the sync client would read as this page
            // failing, for ever.
            if (withdrawn || string.IsNullOrWhiteSpace(capture.Title)) return InboxIntakeOutcome.Ignored;

            // A channel that knows the link says so on the capture; one that
            // does not leaves it null and the title is read for one, as a
            // phone's one-line capture always has been.
            var sourceUrl = string.IsNullOrWhiteSpace(capture.SourceUrl)
                ? ContentKindDetector.FirstUrl(capture.Title)
                : capture.SourceUrl.Trim();
            var bodyMd = string.IsNullOrWhiteSpace(capture.BodyMd) ? null : capture.BodyMd.Trim();

            var item = InboxItem.FromCapture(
                capture.Id,
                capture.Title,
                new InboxSource(InboxEnumMap.NormalizeChannel(capture.Channel), Person: null),
                sourceUrl,
                ContentKindDetector.Detect(capture.Title, capture.SourceUrl, bodyMd),
                capture.CapturedAt,
                clock.GetUtcNow(),
                bodyMd,
                capture.ReplicaBacked);

            await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

            return InboxIntakeOutcome.Received;
        }

        // Open is what a tombstone can still close: unprocessed or deferred.
        // A deferred item was put aside, not decided, and the phone dismissing
        // it is the same dismissal it would be on an unprocessed one.
        if (!withdrawn || !existing.IsOpen) return InboxIntakeOutcome.AlreadyKnown;

        existing.Archive(clock.GetUtcNow());

        // The replica is the source of this tombstone, so there is nothing to
        // tell it. Archive() set the flag because it cannot know who is asking;
        // clearing it here is what keeps this desktop from echoing the phone's
        // own deletion back at it.
        existing.MarkReplicaAcknowledged();

        await items.SaveAsync(existing, cancellationToken).ConfigureAwait(false);

        return InboxIntakeOutcome.Withdrawn;
    }
}
