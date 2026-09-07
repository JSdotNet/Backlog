using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Ports;

/// <summary>
/// Where live pairing codes are kept, by hash. Short-lived state: an
/// implementation is free to drop a row once it has expired, and nothing here
/// depends on being able to read one back afterwards.
/// </summary>
public interface IPairingCodeStore
{
    /// <summary>Stores a freshly issued code.</summary>
    Task Add(PairingCode code, CancellationToken cancellationToken = default);

    /// <summary>
    /// The code with this hash, or null. The hash is the only handle there is —
    /// the plaintext was shown once and never stored — so a redemption that
    /// misses cannot say which code it was aiming at.
    /// </summary>
    Task<PairingCode?> FindByHash(string codeHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes a code, and reports whether this caller was the one that did.
    /// False means somebody else redeemed it first, which the handler has to
    /// treat as a used code rather than as a success — a check-then-write would
    /// let two devices through one code.
    /// </summary>
    Task<bool> TryBurn(string codeHash, CancellationToken cancellationToken = default);
}
