namespace Backlog.Modules.Sync.Abstractions;

/// <summary>
/// The stable, machine-readable codes the sync module puts on an
/// <c>Error</c>. The API turns each one into a ProblemDetails <c>type</c> URI
/// (inherited ADR 0017), so a client can branch on the code instead of
/// pattern-matching an English sentence.
/// </summary>
public static class SyncErrorCodes
{
    /// <summary>No live code hashes to what was typed. Covers a typo and a code
    /// that was never issued alike — the service cannot tell them apart and
    /// deliberately does not try.</summary>
    public const string PairingCodeNotFound = "pairing.code_not_found";

    /// <summary>The code existed and its ten minutes are up.</summary>
    public const string PairingCodeExpired = "pairing.code_expired";

    /// <summary>The code existed and has already paired a device. Codes are
    /// single-use.</summary>
    public const string PairingCodeUsed = "pairing.code_used";

    /// <summary>What was typed is not a pairing code at all — wrong length once
    /// normalized, or nothing but characters outside the alphabet.</summary>
    public const string PairingCodeMalformed = "pairing.code_malformed";

    /// <summary>The registration credential does not match the one on file for
    /// that device. Mapped to 401, not 404: it is a failed authentication, and
    /// saying which of the two was wrong would be a hint.</summary>
    public const string DeviceCredentialInvalid = "device.credential_invalid";

    /// <summary>A device has to be called something, so a person can tell two of
    /// them apart in a list.</summary>
    public const string DeviceNameRequired = "device.name_required";

    /// <summary>No capture with that id is waiting for the calling owner. An id
    /// belonging to somebody else reads the same way, because the lookup starts
    /// from the owner and never sees it.</summary>
    public const string InboxItemNotFound = "inbox.item_not_found";

    /// <summary>A capture whose title or source is missing or longer than the
    /// service will store. Refused at the edge, so an oversized capture is a 400
    /// naming the field rather than a store failure nobody can act on.</summary>
    public const string CaptureInvalid = "inbox.capture_invalid";

    /// <summary>More task changes in one push than the service will take. The
    /// client batches well below the cap, so reaching it is either a client that
    /// stopped batching or a caller that is not one of ours.</summary>
    public const string PushBatchTooLarge = "sync.push_batch_too_large";

    /// <summary>More session records in one push than the service will take.
    /// Its own code rather than sharing <see cref="PushBatchTooLarge"/>: the two
    /// caps are different numbers over different containers, and a device told
    /// only "too large" would not know which of its two pushes to make
    /// smaller.</summary>
    public const string SessionBatchTooLarge = "sync.session_batch_too_large";

    /// <summary>A session record the service will not store — an empty session
    /// id or agent kind, or a free-text field longer than the whitelist allows.
    /// Refused at the edge and for the whole batch, so an oversized record is a
    /// 400 naming the field rather than a store failure nobody can act
    /// on.</summary>
    public const string SessionInvalid = "sync.session_invalid";

    /// <summary>One session record is larger than the store will take. Its own
    /// code rather than <see cref="TaskTooLarge"/>, which names a task, and
    /// mapped to 413 for the same reason that one is: it is the one replica
    /// failure a retry cannot help with, so the batch carrying it would fail on
    /// every run for ever and it has to be visible as the caller's to fix.
    /// <para>
    /// Reaching it means the edge bounds in <c>SyncRequestLimits</c> and the
    /// store's own ceiling have drifted apart, because every field of a record is
    /// bounded well below two megabytes before it gets here. That is worth
    /// answering precisely rather than folding into "the replica is
    /// unavailable", which would tell the caller to come back and try the same
    /// record again.
    /// </para></summary>
    public const string SessionTooLarge = "sync.session_too_large";

    /// <summary>The <c>since</c> cursor is not one this service minted — wrong
    /// prefix, not base64, or a signature that does not verify. Mapped to 400:
    /// the caller sent something that is not a cursor, and the fix is to drop it
    /// and pull from the beginning.</summary>
    public const string SyncCursorMalformed = "sync.cursor_malformed";

    /// <summary>The cursor verifies, and it names somebody else's owner. Mapped
    /// to 403 rather than 404 on purpose: this is not a caller who mistyped an
    /// id, it is a correctly-signed cursor for another person's feed being
    /// replayed, and the one thing that must not happen is for it to pass
    /// quietly (.arc42/adr/0005 §Consequences).</summary>
    public const string SyncCursorNotYours = "sync.cursor_not_yours";

    /// <summary>The cursor was ours and the store will no longer resume from it.
    /// Raised by the replica adapter rather than by the codec — only the store
    /// knows how far back its feed still reaches. Mapped to 400: the client has
    /// to start over from the beginning.</summary>
    public const string SyncCursorExpired = "sync.cursor_expired";

    /// <summary>One task is larger than the store will take — Cosmos caps a
    /// document at two megabytes. Mapped to 413, and it is the one replica
    /// failure a retry cannot help with: the batch carrying that document would
    /// fail on every run for ever, so it has to be visible as the caller's to
    /// fix.</summary>
    public const string TaskTooLarge = "sync.task_too_large";

    /// <summary>The store throttled the request and it outlived the SDK's own
    /// retries. Mapped to 429: come back, and more slowly. Distinct from
    /// <see cref="ReplicaUnavailable"/> because the store is up and answering —
    /// it is this caller's rate that is the problem.</summary>
    public const string ReplicaBusy = "sync.replica_busy";

    /// <summary>The replica is not reachable yet. Mapped to 503, because the
    /// caller should come back rather than change anything: locally this is the
    /// Cosmos emulator still starting, and nothing in the app model waits on
    /// it.</summary>
    public const string ReplicaUnavailable = "sync.replica_unavailable";
}
