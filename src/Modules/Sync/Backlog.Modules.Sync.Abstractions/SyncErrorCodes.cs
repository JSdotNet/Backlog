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
}
