using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Backlog.Infrastructure;

/// <summary>
/// The one DPAPI seal every per-user secret on disk is kept under (local ADR 0030):
/// <see cref="ProtectedData"/> under <see cref="DataProtectionScope.CurrentUser"/>, no
/// entropy, the envelope as base64 text. Another account on the machine, or the file
/// copied to another machine, opens nothing.
/// <para>
/// <see cref="SecretEnvelope"/> seals one value in a settings file with it; the sync
/// device credential, the device signing key and the spec-manager tokens seal a whole
/// file. The format is the
/// one each of them wrote before they shared it, so what is already on disk still
/// opens.
/// </para>
/// <para>
/// Opening throws what <see cref="Convert.FromBase64String(string)"/> and
/// <see cref="ProtectedData.Unprotect"/> throw — <see cref="FormatException"/> and
/// <see cref="CryptographicException"/> — because each caller reads an envelope it
/// cannot open its own way: no secret, no credential, nothing kept.
/// </para>
/// </summary>
/// <remarks>
/// Compiled into the Claude, Azure Foundry, file-system, Sync and spec-manager adapters as a linked
/// <c>Compile</c> item, the way <see cref="SecretEnvelope"/> and <c>IsoTimestamps</c>
/// are: those adapters share no project this one could live in without a new reference.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class UserDpapi
{
    /// <summary><paramref name="plaintext"/> sealed under the current user, as base64.
    /// The caller clears its own buffer.</summary>
    internal static string Seal(byte[] plaintext) =>
        Convert.ToBase64String(ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser));

    /// <summary>The bytes <paramref name="envelope"/> seals. The caller clears the
    /// buffer it gets back once it has read it.</summary>
    internal static byte[] Open(string envelope) =>
        ProtectedData.Unprotect(Convert.FromBase64String(envelope), optionalEntropy: null, DataProtectionScope.CurrentUser);
}
