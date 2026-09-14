namespace Backlog.Modules.Inbox.Abstractions.Services;

/// <summary>One acknowledgement waiting to leave the machine: which replica
/// capture it is, enough of it to rebuild the tombstone document, and when the
/// desktop decided.</summary>
public sealed record InboxCaptureAckDto(
    Guid CaptureId,
    string Title,
    DateTimeOffset CapturedAt,
    DateTimeOffset AcknowledgedAt);

/// <summary>
/// The port the sync client drains on every push: captures this desktop has
/// routed or archived and has not yet told the replica about.
/// <para>
/// An outbox rather than a call on the spot, because routing happens offline
/// as readily as online. The flag sits on the item, so an acknowledgement
/// survives a restart and reaches the phone on whichever push first succeeds —
/// and the phone's list drops the capture at that moment rather than never.
/// </para>
/// </summary>
public interface IInboxCaptureOutbox
{
    Task<IReadOnlyList<InboxCaptureAckDto>> ListPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Clears the flag on the items whose tombstones the replica
    /// accepted. Called only after a successful push, so a failed one leaves
    /// them for the next.</summary>
    Task MarkSentAsync(IReadOnlyList<Guid> captureIds, CancellationToken cancellationToken = default);
}
