using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Ports;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;

namespace Backlog.Infrastructure.Capture.Inbox;

/// <summary>
/// Answers Capture's <see cref="ICaptureDelivery"/> port over the Inbox's
/// published <see cref="IInboxIntake"/>: the one place a capture becomes an
/// inbox capture.
/// <para>
/// Here and not in either module, for the reason the Inbox's own backlog
/// target lives under <c>src/Infrastructure</c>: Capture may not see the
/// Inbox, and the Inbox does not know Capture exists. What is translated is
/// small and worth having in one place — the kind becomes the channel slug
/// the Inbox badge reads, the id travels as it is so the intake's own
/// idempotency does the deduplication, and the intake's four answers fold
/// into the run's three. <c>Withdrawn</c> cannot happen for a live capture and
/// is read as already known because that is what it would mean if it did.
/// And the capture says it is not replica-backed: the replica has never seen
/// this id, so there is nothing to acknowledge to it when the reader routes
/// or archives the item.
/// </para>
/// <para>
/// Where a capture came from rides on the Inbox's source channel and nowhere
/// else: no <c>#capture/&lt;source&gt;</c> tag is put on the item, because the
/// domain's Tag concept is still a draft and a convention minted here would
/// outlive whatever it decides.
/// </para>
/// <para>
/// The facts an import manifest adds travel the same way: the content kind,
/// tags and person as the intake already takes them, and the list by name.
/// A name is looked up among the Inbox's own lists — once per run, since this
/// adapter lives for one — and filed by id when it is there. When it is not,
/// the item still lands, unfiled, and the answer says so: an import never
/// creates a list (local ADR 0017), because lists are the reader's organisation.
/// </para>
/// </summary>
internal sealed class InboxCaptureDelivery(IInboxIntake intake, IInboxListLookup? lists = null) : ICaptureDelivery
{
    private IReadOnlyList<InboxListDto>? _lists;

    public async Task<CaptureDeliveryOutcome> DeliverAsync(CaptureItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var facts = item.Facts;
        var listId = facts?.List is { Length: > 0 } name ? await ListIdAsync(name, cancellationToken).ConfigureAwait(false) : null;
        var unfiled = facts?.List is { Length: > 0 } && listId is null;

        var capture = new InboxCaptureDto(
            item.Id,
            item.Title,
            CaptureSourceKinds.Slug(item.Kind),
            item.CapturedAt,
            UpdatedAt: item.CapturedAt,
            WithdrawnAt: null,
            item.SourceUrl,
            item.BodyMd,
            ReplicaBacked: false,
            facts?.Tags,
            facts?.Person,
            facts?.ContentKind,
            listId);

        var outcome = await intake.ReceiveAsync(capture, cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            InboxIntakeOutcome.Received => unfiled ? CaptureDeliveryOutcome.DeliveredUnfiled : CaptureDeliveryOutcome.Delivered,
            InboxIntakeOutcome.AlreadyKnown or InboxIntakeOutcome.Withdrawn => CaptureDeliveryOutcome.AlreadyKnown,
            _ => CaptureDeliveryOutcome.Ignored
        };
    }

    /// <summary>The id of the list called <paramref name="name"/>, matched the
    /// way a reader would — trimmed, and without regard to case — or null when
    /// there is none.</summary>
    private async Task<Guid?> ListIdAsync(string name, CancellationToken cancellationToken)
    {
        if (lists is null) return null;

        _lists ??= await lists.ListsAsync(cancellationToken).ConfigureAwait(false);

        return _lists.FirstOrDefault(list => string.Equals(list.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;
    }
}
