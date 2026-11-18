namespace Backlog.Modules.Sync.DomainModels;

/// <summary>
/// One registered machine. It proves itself either with a long-lived registration
/// credential, of which the service holds only <paramref name="CredentialHash"/>, or
/// with a non-exportable key whose public half is <paramref name="PublicKey"/> (local
/// ADR 0052 §2). Neither is a secret here, so a copy of this record is not a copy of
/// one. A device holds one or the other: binding a key drops the credential hash.
/// </summary>
/// <param name="Id">This device.</param>
/// <param name="OwnerId">Whose data it may see.</param>
/// <param name="Name">What a person calls it in a device list.</param>
/// <param name="CredentialHash">SHA-256 of the registration credential, hex; null
/// for a device bound to a key.</param>
/// <param name="RegisteredAt">When it first came in — first registration or
/// pairing, the record does not distinguish.</param>
/// <param name="PairedBy">The device that issued the pairing code this one redeemed;
/// null for the first device of an owner, and for a device registered before the
/// field existed. What lets the issuing desktop ask whether to keep the device.</param>
/// <param name="PublicKey">The device's P-256 public key, base64url SubjectPublicKeyInfo;
/// null for a device still on a credential.</param>
public sealed record Device(
    DeviceId Id,
    OwnerId OwnerId,
    string Name,
    string? CredentialHash,
    DateTimeOffset RegisteredAt,
    DeviceId? PairedBy = null,
    string? PublicKey = null)
{
    /// <summary>Whether the device proves itself by signing rather than by its
    /// credential. A key-bound device's credential path is closed.</summary>
    public bool IsKeyBound => PublicKey is not null;
}
