namespace Backlog.Modules.Sync.DomainModels;

/// <summary>
/// One registered machine. It holds a long-lived registration credential; the
/// service holds only <paramref name="CredentialHash"/>, so a copy of this
/// record is not a copy of the secret.
/// </summary>
/// <param name="Id">This device.</param>
/// <param name="OwnerId">Whose data it may see.</param>
/// <param name="Name">What a person calls it in a device list.</param>
/// <param name="CredentialHash">SHA-256 of the registration credential, hex.</param>
/// <param name="RegisteredAt">When it first came in — first registration or
/// pairing, the record does not distinguish.</param>
/// <param name="PairedBy">The device that issued the pairing code this one redeemed;
/// null for the first device of an owner, and for a device registered before the
/// field existed. What lets the issuing desktop ask whether to keep the device.</param>
public sealed record Device(
    DeviceId Id,
    OwnerId OwnerId,
    string Name,
    string CredentialHash,
    DateTimeOffset RegisteredAt,
    DeviceId? PairedBy = null);
