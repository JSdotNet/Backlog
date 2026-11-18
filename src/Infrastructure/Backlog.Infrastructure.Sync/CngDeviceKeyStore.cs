using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The Windows device key (local ADR 0052 §2): a persisted, user-scoped CNG
/// ECDSA P-256 key that cannot be exported.
/// <para>
/// The TPM comes first, through the Platform Crypto Provider, where the private key
/// never exists outside the chip and a copy of the profile cannot sign. A machine
/// with no usable TPM gets the same key in the Software Key Storage Provider instead,
/// still non-exportable and sealed to the user, and the store reports
/// <see cref="DeviceKeyProtection.Software"/> so Settings can say so. Opening looks in
/// the same order, so a key created once is the key found on every later start.
/// </para>
/// <para>
/// A failure is not remembered: only an opened key is kept, so a TPM that refused once
/// is asked again on the next call.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CngDeviceKeyStore : IDeviceKeyStore, IDisposable
{
    /// <summary>The name the installed app's key is kept under.</summary>
    public const string DefaultKeyName = "Backlog.Sync.DeviceKey";

    private readonly string _keyName;
    private readonly Lock _gate = new();
    private EcdsaDeviceKey? _key;
    private DeviceKeyProtection _protection;

    /// <param name="keyName">The CNG key name. A test passes a throwaway one and
    /// deletes it afterwards.</param>
    public CngDeviceKeyStore(string keyName = DefaultKeyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);
        _keyName = keyName;
    }

    public DeviceKeyProtection Protection
    {
        get
        {
            _ = GetOrCreate();
            return _protection;
        }
    }

    public IDeviceKey GetOrCreate()
    {
        lock (_gate)
        {
            if (_key is not null) return _key;

            var (cng, protection) = OpenOrCreate();
            using (cng)
            {
                // ECDsaCng takes its own handle on the key, so the CngKey is let go here.
                _key = new EcdsaDeviceKey(new ECDsaCng(cng));
            }

            _protection = protection;
            return _key;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _key?.Dispose();
            _key = null;
        }
    }

    /// <summary>
    /// The key where it is, else a new one. A key that exists and fails to open is
    /// thrown, never replaced: a TPM key that failed once — lockout, a busy device —
    /// would otherwise be shadowed for good by a software key under its name, and the
    /// device bound to it would be refused. Software is used only when the Platform
    /// Crypto Provider is genuinely not there (<see cref="CngKeyFailures"/>).
    /// </summary>
    private (CngKey Key, DeviceKeyProtection Protection) OpenOrCreate()
    {
        var platformAvailable = true;

        try
        {
            if (CngKey.Exists(_keyName, CngProvider.MicrosoftPlatformCryptoProvider))
            {
                return (CngKey.Open(_keyName, CngProvider.MicrosoftPlatformCryptoProvider), DeviceKeyProtection.Hardware);
            }
        }
        catch (CryptographicException ex) when (CngKeyFailures.Classify(ex.HResult) == CngKeyFailure.PlatformUnavailable)
        {
            // No TPM here: the Platform Crypto Provider answers that rather than "no
            // such key", so the key, if any, is in software.
            platformAvailable = false;
        }

        if (CngKey.Exists(_keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider))
        {
            return (CngKey.Open(_keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider), DeviceKeyProtection.Software);
        }

        if (platformAvailable)
        {
            try
            {
                return (CreateOrOpen(CngProvider.MicrosoftPlatformCryptoProvider), DeviceKeyProtection.Hardware);
            }
            catch (CryptographicException ex) when (CngKeyFailures.Classify(ex.HResult) == CngKeyFailure.PlatformUnavailable)
            {
                // The provider is there but the TPM behind it is missing or switched
                // off: the software provider is the fallback, and Settings says so.
            }
        }

        return (CreateOrOpen(CngProvider.MicrosoftSoftwareKeyStorageProvider), DeviceKeyProtection.Software);
    }

    /// <summary>A new key in <paramref name="provider"/>, or the one another start of
    /// the app made there a moment ago.</summary>
    private CngKey CreateOrOpen(CngProvider provider)
    {
        try
        {
            return CngKey.Create(CngAlgorithm.ECDsaP256, _keyName, new CngKeyCreationParameters
            {
                Provider = provider,
                ExportPolicy = CngExportPolicies.None,
                KeyUsage = CngKeyUsages.Signing,
                KeyCreationOptions = CngKeyCreationOptions.None,
            });
        }
        catch (CryptographicException ex) when (CngKeyFailures.Classify(ex.HResult) == CngKeyFailure.KeyExists)
        {
            return CngKey.Open(_keyName, provider);
        }
    }
}
