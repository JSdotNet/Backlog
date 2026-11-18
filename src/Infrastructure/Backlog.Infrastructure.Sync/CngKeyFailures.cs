namespace Backlog.Infrastructure.Sync;

/// <summary>What a CNG failure means for <see cref="CngDeviceKeyStore"/>.</summary>
internal enum CngKeyFailure
{
    /// <summary>Anything else, a transient failure included — a TPM in lockout, a
    /// denied access, a busy device. Thrown, never fallen back from.</summary>
    Other,

    /// <summary>The provider is not on this machine, or the TPM behind it is missing,
    /// switched off or not ready: the one reason to keep the key in software.</summary>
    PlatformUnavailable,

    /// <summary>A key by that name is already there — made by another start of the app
    /// between the look and the create — and is opened rather than made again.</summary>
    KeyExists,
}

/// <summary>
/// Reads a <see cref="System.Security.Cryptography.CryptographicException"/>'s HResult
/// for <see cref="CngDeviceKeyStore"/>. Deliberately narrow: a software key under the
/// device key's name is made only when the TPM is genuinely not there, because one made
/// beside a TPM key that merely failed once would shadow it from then on.
/// </summary>
internal static class CngKeyFailures
{
    private const int NteExists = unchecked((int)0x8009000F);

    private static readonly HashSet<int> PlatformUnavailableCodes =
    [
        unchecked((int)0x8009001E), // NTE_PROV_DLL_NOT_FOUND
        unchecked((int)0x80090029), // NTE_NOT_SUPPORTED
        unchecked((int)0x80090030), // NTE_DEVICE_NOT_READY
        unchecked((int)0x80090035), // NTE_DEVICE_NOT_FOUND
        unchecked((int)0x80284008), // TBS_E_SERVICE_NOT_RUNNING
        unchecked((int)0x8028400F), // TBS_E_TPM_NOT_FOUND
        unchecked((int)0x80284010), // TBS_E_SERVICE_DISABLED
        unchecked((int)0x80280006), // TPM_E_DISABLED
        unchecked((int)0x80280007), // TPM_E_DEACTIVATED
        unchecked((int)0x80290101), // TPM_E_PCP_DEVICE_NOT_READY
    ];

    public static CngKeyFailure Classify(int hResult) =>
        hResult == NteExists ? CngKeyFailure.KeyExists
        : PlatformUnavailableCodes.Contains(hResult) ? CngKeyFailure.PlatformUnavailable
        : CngKeyFailure.Other;
}
