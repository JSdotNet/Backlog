using System.Security.Cryptography;
using System.Text;

namespace Backlog.Infrastructure;

/// <summary>
/// One secret value in a per-user settings file, sealed under the signed-in
/// Windows user (local ADR 0030): a GitHub token, the Claude admin key, the Azure
/// Foundry API key.
/// <para>
/// The envelope is on the value rather than on the file, so the files stay
/// legible JSON and only the secret becomes <c>dpapi:</c> followed by the base64
/// of a <see cref="ProtectedData"/> envelope under
/// <see cref="DataProtectionScope.CurrentUser"/> — the envelope
/// <c>DpapiDeviceCredentialStore</c> uses for the sync credential, and for the same
/// reason: another account on the machine, or the file copied to another machine,
/// opens nothing.
/// </para>
/// <para>
/// A value without the prefix is plaintext an earlier release wrote. It is read as
/// it is and reported, so the store can seal it straight away rather than leave it
/// in the clear until somebody next saves. An envelope this user cannot open reads
/// as no secret: the recovery is entering it again in Settings, which an empty key
/// field already offers.
/// </para>
/// <para>
/// A platform without DPAPI — only a development harness off Windows, since the
/// stores that use this are desktop-only — keeps the value as it was, plaintext,
/// rather than refusing to keep it.
/// </para>
/// </summary>
/// <remarks>
/// One copy for every settings adapter, compiled into the Claude and Azure Foundry
/// adapters as a linked <c>Compile</c> item the way <c>IsoTimestamps</c> is.
/// </remarks>
internal static class SecretEnvelope
{
    /// <summary>What marks a stored value as sealed.</summary>
    internal const string Prefix = "dpapi:";

    /// <summary>The form <paramref name="secret"/> is written in; null or blank
    /// stays null.</summary>
    internal static string? Seal(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return null;
        if (!OperatingSystem.IsWindows()) return secret;

        var plaintext = Encoding.UTF8.GetBytes(secret);

        try
        {
            return Prefix + Convert.ToBase64String(
                ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser));
        }
        finally
        {
            Array.Clear(plaintext);
        }
    }

    /// <summary>
    /// The secret a stored value holds, or null when there is none or this user
    /// cannot open it. <paramref name="wasPlaintext"/> is true for a value an
    /// earlier release wrote in the clear on a platform that can seal it — the
    /// caller's cue to write it back sealed.
    /// </summary>
    internal static string? Open(string? stored, out bool wasPlaintext)
    {
        wasPlaintext = false;

        if (string.IsNullOrWhiteSpace(stored)) return null;

        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            wasPlaintext = OperatingSystem.IsWindows();
            return stored;
        }

        if (!OperatingSystem.IsWindows() || stored.Length == Prefix.Length) return null;

        try
        {
            var plaintext = ProtectedData.Unprotect(
                Convert.FromBase64String(stored[Prefix.Length..]),
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);

            var secret = Encoding.UTF8.GetString(plaintext);
            Array.Clear(plaintext);

            return string.IsNullOrWhiteSpace(secret) ? null : secret;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }
}
