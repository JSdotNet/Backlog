namespace Backlog.Modules.Sync.DomainModels;

/// <summary>
/// A live invitation to join an owner. Not a credential: it authorizes exactly
/// one act, minting a registration credential, and only within its window.
/// <para>
/// Stored by hash for the same reason the device credential is — the plaintext
/// exists on the issuing device's screen and nowhere else. That also means a
/// redemption attempt that finds nothing cannot be attributed to any particular
/// code, which is why there is no failed-attempt counter here; see
/// <c>RedeemPairingCodeCommandHandler</c> for what stands in its place.
/// </para>
/// </summary>
/// <param name="CodeHash">SHA-256 of the normalized code, hex. The lookup key.</param>
/// <param name="OwnerId">The owner a redeeming device joins.</param>
/// <param name="IssuedBy">Which already-paired device read the code out.</param>
/// <param name="ExpiresAt">When it stops working, issued plus ten minutes.</param>
/// <param name="Redeemed">Set once, on the redemption that consumed it.</param>
public sealed record PairingCode(
    string CodeHash,
    OwnerId OwnerId,
    DeviceId IssuedBy,
    DateTimeOffset ExpiresAt,
    bool Redeemed = false)
{
    /// <summary>Whether the window has closed at <paramref name="now"/>.</summary>
    public bool HasExpired(DateTimeOffset now) => now >= ExpiresAt;
}
