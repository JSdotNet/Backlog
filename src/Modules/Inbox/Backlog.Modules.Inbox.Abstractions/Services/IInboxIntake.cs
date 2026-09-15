using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Inbox.Abstractions.Services;

/// <summary>What the intake did with one capture. Four answers rather than a
/// bool, because the sync client counts the first two as work applied and holds
/// the other two — and a person reading a sync summary deserves "already had
/// it" and "nothing to do" to be different from "written".</summary>
public enum InboxIntakeOutcome
{
    /// <summary>A new item was created from a live capture.</summary>
    Received,

    /// <summary>A known, still-unprocessed item was archived because the
    /// replica now carries a tombstone for it.</summary>
    Withdrawn,

    /// <summary>The id is already here and nothing about the capture changes
    /// what this machine has decided — a replay, an echo, or a tombstone for an
    /// item this desktop already routed or archived.</summary>
    AlreadyKnown,

    /// <summary>A tombstone for an id this machine has never seen, or a live
    /// capture with no title to keep. There is nothing to withdraw and nothing
    /// to make.</summary>
    Ignored
}

/// <summary>
/// The port the sync client hands a pulled capture to.
/// <para>
/// Separate from <see cref="IInboxItems"/> because the caller is not a screen:
/// the client has a replica document and a cancellation token, and nothing
/// else about the inbox is its business. Keeping the intake on its own port is
/// also what lets a head without an inbox store — the phone — compose the sync
/// client with this left null and leave captures on the replica.
/// </para>
/// </summary>
public interface IInboxIntake
{
    Task<InboxIntakeOutcome> ReceiveAsync(InboxCaptureDto capture, CancellationToken cancellationToken = default);
}
