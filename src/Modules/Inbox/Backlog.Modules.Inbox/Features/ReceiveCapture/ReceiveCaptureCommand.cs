using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
/// <para>
/// A new item takes the capture's person as its <see cref="InboxSource.Person"/>,
/// stored with its <c>@</c>, and the capture's tags through
/// <see cref="InboxItem.SetTags"/> — bare and de-duplicated. A tag that reads as
/// a person (<c>@bob</c>, <c>#@bob</c>) is refused as a tag, the way the aggregate
/// refuses it everywhere; here that means dropped rather than thrown, so the
/// capture still lands, and the person arrives through its own field or not at
/// all.
/// </para>
/// <para>
/// <b>Files.</b> A capture that names attachments (local ADR 0014) becomes an
/// item that carries them. The metadata is recorded and the item saved
/// <em>before</em> any byte is fetched, so the item is in the inbox whatever
/// the fetches do; each file is then fetched into the item's folder and the
/// item saved again with what happened. A file that could not be fetched is
/// recorded on the item with its reason — see
/// <see cref="InboxAttachmentDownloads"/> — and the outcome is still
/// <see cref="InboxIntakeOutcome.Received"/>: the capture landed, and a page
/// pull that failed over one file would stall every capture behind it.
/// </para>
/// <para>
/// A known item arriving again records any file it did not have yet and
/// fetches only the files never tried. A file that failed waits for a person to
/// press Retry rather than being fetched on every replay: the likeliest reason
/// it failed is that the blob is gone, and asking again each cycle would only
/// repeat the answer. A replay therefore downloads nothing twice.
/// </para>
/// <para>
/// Both attachment ports are optional. A head composed without them records
/// the files by name and fetches nothing, which is the honest answer on a
/// machine with nowhere to fetch from. The logger a failed fetch is reported
/// to is optional too; without one the failure is only on the row.
/// </para>
/// <para>
/// A capture that states its content kind keeps it rather than being read by
/// <see cref="ContentKindDetector"/>, and one that names a list is filed there.
/// Both are an import manifest's facts (local ADR 0017).
/// </para>
/// </summary>
public sealed class ReceiveCaptureCommandHandler(
    IInboxItemRepository items,
    TimeProvider clock,
    IInboxAttachmentSource? attachmentSource = null,
    IInboxAttachmentFiles? attachmentFiles = null,
    ILogger<ReceiveCaptureCommandHandler>? logger = null)
    : ICommandHandler<ReceiveCaptureCommand, Result<InboxIntakeOutcome>>
{
    private readonly ILogger _logger = logger ?? NullLogger<ReceiveCaptureCommandHandler>.Instance;

    public async Task<Result<InboxIntakeOutcome>> Handle(
        ReceiveCaptureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var capture = command.Capture;
        var existing = await items.GetAsync(capture.Id, cancellationToken).ConfigureAwait(false);
        var withdrawn = capture.WithdrawnAt is not null;

        // Deleted here while the phone was still offering it: not a new
        // capture. A replay is known already; the phone withdrawing it itself
        // leaves nothing to tell it, so the acknowledgement goes too.
        if (existing is null
            && await items.GetDeletedCaptureAsync(capture.Id, cancellationToken).ConfigureAwait(false) is not null)
        {
            if (!withdrawn) return InboxIntakeOutcome.AlreadyKnown;

            await items.ForgetDeletedCaptureAsync(capture.Id, cancellationToken).ConfigureAwait(false);
            return InboxIntakeOutcome.Withdrawn;
        }

        // Deleted here after a feed or an import brought it: the source
        // offering it again is not a new capture.
        if (existing is null && await items.WasDismissedAsync(capture.Id, cancellationToken).ConfigureAwait(false))
        {
            return withdrawn ? InboxIntakeOutcome.Ignored : InboxIntakeOutcome.AlreadyKnown;
        }

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
            var attachments = AttachmentsOf(capture);

            var item = InboxItem.FromCapture(
                capture.Id,
                capture.Title,
                new InboxSource(InboxEnumMap.NormalizeChannel(capture.Channel), PersonOf(capture.Person)),
                sourceUrl,
                ContentKindDetector.Detect(
                    capture.Title,
                    capture.SourceUrl,
                    bodyMd,
                    [.. attachments.Select(attachment => attachment.ContentType)]),
                capture.CapturedAt,
                clock.GetUtcNow(),
                bodyMd,
                capture.ReplicaBacked);

            item.RecordAttachments(attachments);

            // A channel that says what it captured is believed; the slug is kept
            // as written so a kind this build does not know survives, the way a
            // stored row's does.
            if (!string.IsNullOrWhiteSpace(capture.Kind))
            {
                item.SetKind(InboxEnumMap.ParseKind(capture.Kind), capture.Kind);
            }

            // Filing is not triage: the item stays unprocessed in the list.
            if (capture.ListId is { } listId) item.MoveToList(listId);

            if (capture.Tags is { Count: > 0 } tags)
            {
                // Born with no tags, so this is a first assignment, not a
                // replacement — and the only way in is the aggregate's own rule.
                item.SetTags(tags
                    .Where(tag => !ReadsAsPerson(tag))
                    .Select(tag => new InboxTag(tag, AutoGenerated: false)));
            }

            // Saved before a byte is fetched: the item is in the inbox whatever
            // the fetches below do.
            await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

            if (await FetchWaitingAsync(item, cancellationToken).ConfigureAwait(false))
            {
                await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);
            }

            return InboxIntakeOutcome.Received;
        }

        if (!withdrawn)
        {
            // A replay, or an item made before this build read files: record
            // what it lacks and fetch what was never tried. Still AlreadyKnown —
            // the capture changed nothing this machine had decided.
            var added = existing.RecordAttachments(AttachmentsOf(capture));
            var fetched = await FetchWaitingAsync(existing, cancellationToken).ConfigureAwait(false);

            if (added > 0 || fetched) await items.SaveAsync(existing, cancellationToken).ConfigureAwait(false);

            return InboxIntakeOutcome.AlreadyKnown;
        }

        // Open is what a tombstone can still close: unprocessed or deferred.
        // A deferred item was put aside, not decided, and the phone dismissing
        // it is the same dismissal it would be on an unprocessed one.
        if (!existing.IsOpen) return InboxIntakeOutcome.AlreadyKnown;

        existing.Archive(clock.GetUtcNow());

        // The replica is the source of this tombstone, so there is nothing to
        // tell it. Archive() set the flag because it cannot know who is asking;
        // clearing it here is what keeps this desktop from echoing the phone's
        // own deletion back at it.
        existing.MarkReplicaAcknowledged();

        await items.SaveAsync(existing, cancellationToken).ConfigureAwait(false);

        return InboxIntakeOutcome.Withdrawn;
    }

    /// <summary>Fetches every file on the item that has never been tried, and
    /// answers whether any was. Nothing to do without both ports.</summary>
    private async Task<bool> FetchWaitingAsync(InboxItem item, CancellationToken cancellationToken)
    {
        if (attachmentSource is null || attachmentFiles is null) return false;

        var waiting = item.Attachments.Where(attachment => attachment.IsWaiting).Select(attachment => attachment.Id).ToList();

        foreach (var attachmentId in waiting)
        {
            await InboxAttachmentDownloads
                .DownloadAsync(item, attachmentId, attachmentSource, attachmentFiles, clock, _logger, cancellationToken)
                .ConfigureAwait(false);
        }

        return waiting.Count > 0;
    }

    /// <summary>The files the capture names, as the aggregate holds them. An
    /// entry no fetch could ever honour — no id, a digest that is not one — is
    /// dropped rather than thrown, for the reason a title-less capture is
    /// ignored: a throw here is a page the sync client replays for ever. The
    /// service refuses such a capture at the edge, so this is a newer or broken
    /// client's document, not a phone's.</summary>
    private static List<InboxAttachment> AttachmentsOf(InboxCaptureDto capture)
    {
        var attachments = new List<InboxAttachment>();

        foreach (var named in capture.Attachments ?? [])
        {
            try
            {
                attachments.Add(InboxAttachment.Named(named.Id, named.Name, named.ContentType, named.SizeBytes, named.Sha256));
            }
            catch (ArgumentException)
            {
                // Dropped; see above.
            }
        }

        return attachments;
    }

    /// <summary>The person with its <c>@</c>, however it was sent, or null for
    /// none. A bare sigil is nobody.</summary>
    private static string? PersonOf(string? person)
    {
        var name = (person ?? string.Empty).Trim().TrimStart('@').Trim();

        return name.Length == 0 ? null : "@" + name;
    }

    /// <summary>The same reading <see cref="InboxItem.SetTags"/> refuses on:
    /// the bare name, after any <c>#</c>, starts with <c>@</c>.</summary>
    private static bool ReadsAsPerson(string? tag) =>
        (tag ?? string.Empty).Trim().TrimStart('#').Trim().StartsWith('@');
}
