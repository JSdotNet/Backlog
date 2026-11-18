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

    private (CngKey Key, DeviceKeyProtection Protection) OpenOrCreate()
    {
        if (TryOpen(CngProvider.MicrosoftPlatformCryptoProvider) is { } hardware) return (hardware, DeviceKeyProtection.Hardware);
        if (TryOpen(CngProvider.MicrosoftSoftwareKeyStorageProvider) is { } software) return (software, DeviceKeyProtection.Software);

        try
        {
            return (Create(CngProvider.MicrosoftPlatformCryptoProvider), DeviceKeyProtection.Hardware);
        }
        catch (CryptographicException)
        {
            // No TPM, a TPM the provider cannot use, or one locked out: the software
            // provider is the fallback, and the Settings page warns about it.
            return (Create(CngProvider.MicrosoftSoftwareKeyStorageProvider), DeviceKeyProtection.Software);
        }
    }

    private CngKey? TryOpen(CngProvider provider)
    {
        try
        {
            return CngKey.Exists(_keyName, provider) ? CngKey.Open(_keyName, provider) : null;
        }
        catch (CryptographicException)
        {
            // The provider itself is unavailable — the Platform Crypto Provider on a
            // machine with no TPM answers this way rather than "no such key".
            return null;
        }
    }

    private CngKey Create(CngProvider provider) =>
        CngKey.Create(CngAlgorithm.ECDsaP256, _keyName, new CngKeyCreationParameters
        {
            Provider = provider,
            ExportPolicy = CngExportPolicies.None,
            KeyUsage = CngKeyUsages.Signing,
            KeyCreationOptions = CngKeyCreationOptions.None,
        });
}
