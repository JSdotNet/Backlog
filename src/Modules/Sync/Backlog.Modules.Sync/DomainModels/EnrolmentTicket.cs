namespace Backlog.Modules.Sync.DomainModels;

/// <summary>Which half of an owner sign-in a ticket stands for.</summary>
public enum EnrolmentTicketKind
{
    /// <summary>A sign-in that left for GitHub and has not come back. Keyed by the
    /// hash of the state the service sent along.</summary>
    SignIn,

    /// <summary>A one-time code the sign-in handed back to the device's loopback.
    /// Keyed by the hash of the code.</summary>
    Code,
}

/// <summary>
/// One step of an owner sign-in (local ADR 0058 §1), stored by hash like a pairing
/// code: the plaintext is in the browser's address bar and nowhere here. Not a
/// credential — a <see cref="EnrolmentTicketKind.Code"/> authorizes one registration,
/// and only with the verifier behind <see cref="CodeChallenge"/>.
/// </summary>
/// <param name="KeyHash">SHA-256 of the state or the code, hex. The lookup key.</param>
/// <param name="Kind">Which step this is.</param>
/// <param name="CodeChallenge">The S256 challenge the device started with.</param>
/// <param name="RedirectUri">The device's loopback, where the answer goes.</param>
/// <param name="ClientState">The device's own state, handed back beside the answer.</param>
/// <param name="ExpiresAt">When it stops working.</param>
/// <param name="Redeemed">Set once, by the call that consumed it.</param>
public sealed record EnrolmentTicket(
    string KeyHash,
    EnrolmentTicketKind Kind,
    string CodeChallenge,
    Uri RedirectUri,
    string ClientState,
    DateTimeOffset ExpiresAt,
    bool Redeemed = false)
{
    /// <summary>Whether the window has closed at <paramref name="now"/>.</summary>
    public bool HasExpired(DateTimeOffset now) => now >= ExpiresAt;
}
