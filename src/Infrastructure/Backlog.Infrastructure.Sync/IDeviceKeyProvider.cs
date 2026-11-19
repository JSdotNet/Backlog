using System.Security.Cryptography;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// One place <see cref="CngDeviceKeyStore"/> can keep its key — the TPM's Platform
/// Crypto Provider, or the Software Key Storage Provider. A seam so the order the store
/// looks in, and what it does with each failure, can be proved on a machine with no TPM.
/// Each call throws <see cref="CryptographicException"/> with the provider's HResult.
/// </summary>
internal interface IDeviceKeyProvider
{
    bool Exists(string keyName);

    ECDsa Open(string keyName);

    /// <summary>A new persisted, non-exportable P-256 signing key under
    /// <paramref name="keyName"/>; NTE_EXISTS when one is already there.</summary>
    ECDsa Create(string keyName);
}
