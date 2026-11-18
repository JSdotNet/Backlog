using System.Security.Cryptography;

using Android.Security.Keystore;

using Backlog.Infrastructure.Sync;

using Java.Security;
using Java.Security.Spec;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Mobile.Services;

/// <summary>
/// The phone's device key (local ADR 0052 §2): an EC P-256 signing key in Android
/// Keystore, made there and never exportable, so a copy of the app's data on another
/// phone cannot sign.
/// </summary>
/// <remarks>
/// <para>
/// StrongBox comes first where the phone has one (Android 9 and later): a separate
/// secure chip, the TPM's counterpart. A phone without one gets the key in the trusted
/// execution environment instead, which Keystore uses on its own; both report
/// <see cref="DeviceKeyProtection.Hardware"/>. Only a phone whose Keystore has neither
/// keeps it in software, and the store says so and logs it.
/// </para>
/// <para>
/// Keystore signs in DER and hands the public half out as SubjectPublicKeyInfo;
/// <see cref="DerSignedDeviceKey"/> turns that into the ES256 the service verifies, and
/// is where that is tested. This class is only the platform calls.
/// </para>
/// <para>
/// As <see cref="CngDeviceKeyStore"/> does, a key that is there and fails to open is
/// thrown, never replaced: the device is bound to it, and a new key under its alias
/// would leave the device refused for good. A failure is not remembered, so the next
/// call asks Keystore again. Every Java failure leaves here as a
/// <see cref="CryptographicException"/>, the one <see cref="SyncTokenProvider"/> and
/// <see cref="DevicePairingClient"/> read as a key that cannot be used.
/// </para>
/// <para>
/// Uninstalling the app deletes the key with the rest of its Keystore entries, as it does
/// the credential beside it, so a fresh install registers again.
/// </para>
/// </remarks>
public sealed class AndroidKeystoreDeviceKeyStore : IDeviceKeyStore
{
    /// <summary>The alias the key is kept under.</summary>
    public const string DefaultAlias = "backlog.sync.device-key";

    private const string Provider = "AndroidKeyStore";
    private const string SignatureAlgorithm = "SHA256withECDSA";

    private readonly string _alias;
    private readonly ILogger _log;
    private readonly Lock _gate = new();
    private DerSignedDeviceKey? _key;
    private DeviceKeyProtection _protection;

    public AndroidKeystoreDeviceKeyStore(ILogger<AndroidKeystoreDeviceKeyStore>? log = null)
        : this(DefaultAlias, log)
    {
    }

    internal AndroidKeystoreDeviceKeyStore(string alias, ILogger<AndroidKeystoreDeviceKeyStore>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        _alias = alias;
        _log = log ?? NullLogger<AndroidKeystoreDeviceKeyStore>.Instance;
    }

    public DeviceKeyProtection Protection
    {
        get
        {
            lock (_gate)
            {
                _ = OpenedKey();
                return _protection;
            }
        }
    }

    public IDeviceKey GetOrCreate()
    {
        lock (_gate)
        {
            return OpenedKey();
        }
    }

    /// <summary>The key, opened or made on the first call. Called under the gate.</summary>
    private DerSignedDeviceKey OpenedKey()
    {
        if (_key is not null) return _key;

        try
        {
            var keyStore = KeyStore.GetInstance(Provider)!;
            keyStore.Load(null);

            if (!keyStore.ContainsAlias(_alias)) Generate();

            if (keyStore.GetEntry(_alias, null) is not KeyStore.PrivateKeyEntry entry)
            {
                throw new CryptographicException("This device's key is in Keystore but is not a private key.");
            }

            var privateKey = entry.PrivateKey
                ?? throw new CryptographicException("This device's key has no private half in Keystore.");
            var publicKey = entry.Certificate?.PublicKey?.GetEncoded()
                ?? throw new CryptographicException("This device's key has no public half in Keystore.");

            _protection = ProtectionOf(privateKey);
            if (_protection == DeviceKeyProtection.Software)
            {
                _log.LogWarning("This device's key {Alias} is kept in software: this phone's Keystore has no secure hardware.", _alias);
            }

            _key = new DerSignedDeviceKey(publicKey, data => Sign(privateKey, data));
            return _key;
        }
        catch (Java.Lang.Throwable ex)
        {
            throw new CryptographicException("This device's key could not be opened or made in Android Keystore.", ex);
        }
    }

    /// <summary>Makes the key, in StrongBox where the phone has one.</summary>
    private void Generate()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            try
            {
                GenerateIn(strongBox: true);
                return;
            }
            catch (Exception ex) when (ex is StrongBoxUnavailableException or ProviderException)
            {
                // No StrongBox on this phone, or one that would not make the key - some
                // answer ProviderException rather than StrongBoxUnavailableException: the
                // trusted execution environment instead.
                _log.LogInformation(ex, "StrongBox could not make this device's key {Alias}, so it is made in the trusted execution environment.", _alias);
            }
        }

        GenerateIn(strongBox: false);
    }

    private void GenerateIn(bool strongBox)
    {
        var builder = new KeyGenParameterSpec.Builder(_alias, KeyStorePurpose.Sign)
            .SetAlgorithmParameterSpec(new ECGenParameterSpec("secp256r1"))!
            .SetDigests(KeyProperties.DigestSha256)!;

        if (strongBox && OperatingSystem.IsAndroidVersionAtLeast(28)) builder.SetIsStrongBoxBacked(true);

        var generator = KeyPairGenerator.GetInstance(KeyProperties.KeyAlgorithmEc, Provider)!;
        generator.Initialize(builder.Build());
        generator.GenerateKeyPair();
    }

    private static DeviceKeyProtection ProtectionOf(IPrivateKey privateKey)
    {
        var factory = KeyFactory.GetInstance(privateKey.Algorithm, Provider)!;
        if (factory.GetKeySpec(privateKey, Java.Lang.Class.FromType(typeof(KeyInfo))) is not KeyInfo info)
        {
            return DeviceKeyProtection.Software;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            return info.SecurityLevel is (int)KeyStoreSecurityLevel.Strongbox or (int)KeyStoreSecurityLevel.TrustedEnvironment or (int)KeyStoreSecurityLevel.UnknownSecure
                ? DeviceKeyProtection.Hardware
                : DeviceKeyProtection.Software;
        }

        return info.IsInsideSecureHardware ? DeviceKeyProtection.Hardware : DeviceKeyProtection.Software;
    }

    /// <summary>A DER <c>SHA256withECDSA</c> signature from Keystore. A fresh
    /// <see cref="Signature"/> each time: one is not safe to share between threads.</summary>
    private static byte[] Sign(IPrivateKey privateKey, byte[] data)
    {
        try
        {
            var signature = Signature.GetInstance(SignatureAlgorithm)!;
            signature.InitSign(privateKey);
            signature.Update(data);
            return signature.Sign()!;
        }
        catch (Java.Lang.Throwable ex)
        {
            throw new CryptographicException("This device's key would not sign in Android Keystore.", ex);
        }
    }
}
