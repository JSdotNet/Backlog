using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Backlog.Infrastructure.Sync;

/// <summary>An <see cref="IDeviceKeyProvider"/> over a CNG key storage provider.</summary>
[SupportedOSPlatform("windows")]
internal sealed class CngKeyProvider(CngProvider provider) : IDeviceKeyProvider
{
    public static CngKeyProvider Platform { get; } = new(CngProvider.MicrosoftPlatformCryptoProvider);

    public static CngKeyProvider Software { get; } = new(CngProvider.MicrosoftSoftwareKeyStorageProvider);

    public bool Exists(string keyName) => CngKey.Exists(keyName, provider);

    public ECDsa Open(string keyName)
    {
        // ECDsaCng takes its own handle on the key, so the CngKey is let go here.
        using var key = CngKey.Open(keyName, provider);
        return new ECDsaCng(key);
    }

    public ECDsa Create(string keyName)
    {
        using var key = CngKey.Create(CngAlgorithm.ECDsaP256, keyName, new CngKeyCreationParameters
        {
            Provider = provider,
            ExportPolicy = CngExportPolicies.None,
            KeyUsage = CngKeyUsages.Signing,
            KeyCreationOptions = CngKeyCreationOptions.None,
        });
        return new ECDsaCng(key);
    }
}
