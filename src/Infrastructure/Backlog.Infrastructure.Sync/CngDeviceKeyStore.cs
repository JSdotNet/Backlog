using System.Runtime.Versioning;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The Windows device key (local ADR 0058 §2): a persisted, user-scoped CNG
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
/// is asked again on the next call. A key kept in software is logged as a warning,
/// with the reason, when it is opened or made.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class CngDeviceKeyStore : IDeviceKeyStore, IDisposable
{
    /// <summary>The name the installed app's key is kept under.</summary>
    public const string DefaultKeyName = "Backlog.Sync.DeviceKey";

    private readonly string _keyName;
    private readonly IDeviceKeyProvider _platform;
    private readonly IDeviceKeyProvider _software;
    private readonly ILogger _log;
    private readonly Lock _gate = new();
    private EcdsaDeviceKey? _key;
    private DeviceKeyProtection _protection;

    /// <param name="keyName">The CNG key name. A test passes a throwaway one and
    /// deletes it afterwards.</param>
    /// <param name="log">Where a key kept in software, and why, is written.</param>
    public CngDeviceKeyStore(string keyName = DefaultKeyName, ILogger<CngDeviceKeyStore>? log = null)
        : this(keyName, CngKeyProvider.Platform, CngKeyProvider.Software, log)
    {
    }

    /// <summary>Over stand-in providers, so the order the key is looked for in can be
    /// proved without a TPM.</summary>
    internal CngDeviceKeyStore(
        string keyName,
        IDeviceKeyProvider platform,
        IDeviceKeyProvider software,
        ILogger<CngDeviceKeyStore>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(software);

        _keyName = keyName;
        _platform = platform;
        _software = software;
        _log = log ?? NullLogger<CngDeviceKeyStore>.Instance;
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

    public void Dispose()
    {
        lock (_gate)
        {
            _key?.Dispose();
            _key = null;
        }
    }

    /// <summary>The key, opened or made on the first call. Called under the gate.</summary>
    private EcdsaDeviceKey OpenedKey()
    {
        if (_key is not null) return _key;

        var (ecdsa, protection) = OpenOrCreate();
        _key = new EcdsaDeviceKey(ecdsa);
        _protection = protection;
        return _key;
    }

    /// <summary>
    /// The key where it is, else a new one. A key that exists and fails to open is
    /// thrown, never replaced: a TPM key that failed once — lockout, a busy device —
    /// would otherwise be shadowed for good by a software key under its name, and the
    /// device bound to it would be refused. Software is used only when the Platform
    /// Crypto Provider is genuinely not there (<see cref="CngKeyFailures"/>).
    /// <para>
    /// A TPM that cannot even be asked whether it holds the key still lets a key already
    /// in software be found — that key is the device's, and opening it shadows nothing —
    /// but no key is made then: the key may well be in the TPM, so its failure is thrown.
    /// </para>
    /// </summary>
    private (ECDsa Key, DeviceKeyProtection Protection) OpenOrCreate()
    {
        var platformAvailable = true;
        CryptographicException? probeFailure = null;
        var inPlatform = false;

        try
        {
            inPlatform = _platform.Exists(_keyName);
        }
        catch (CryptographicException ex) when (CngKeyFailures.Classify(ex.HResult) == CngKeyFailure.PlatformUnavailable)
        {
            // No TPM here: the Platform Crypto Provider answers that rather than "no
            // such key", so the key, if any, is in software.
            platformAvailable = false;
        }
        catch (CryptographicException ex)
        {
            probeFailure = ex;
        }

        // Outside the try: a key that is there and will not open is thrown, whatever
        // the code, rather than read as a TPM that is not there.
        if (inPlatform) return (_platform.Open(_keyName), DeviceKeyProtection.Hardware);

        if (_software.Exists(_keyName))
        {
            LogKeyKeptInSoftware(_log, probeFailure, _keyName);
            return (_software.Open(_keyName), DeviceKeyProtection.Software);
        }

        if (probeFailure is not null) ExceptionDispatchInfo.Throw(probeFailure);

        if (platformAvailable)
        {
            // Not CreateOrOpen: a key another start made here that then will not open
            // is thrown from the catch below rather than read as a TPM that is not there.
            try
            {
                return (_platform.Create(_keyName), DeviceKeyProtection.Hardware);
            }
            catch (CryptographicException ex) when (CngKeyFailures.ClassifyCreate(ex.HResult) == CngKeyFailure.KeyExists)
            {
                return (_platform.Open(_keyName), DeviceKeyProtection.Hardware);
            }
            catch (CryptographicException ex) when (CngKeyFailures.ClassifyCreate(ex.HResult) == CngKeyFailure.PlatformUnavailable)
            {
                // The provider is there but the TPM behind it is missing or switched
                // off: the software provider is the fallback, and Settings says so.
                LogTpmCouldNotMakeKey(_log, ex, _keyName);
                return (CreateOrOpen(_software), DeviceKeyProtection.Software);
            }
        }

        LogNoUsableTpm(_log, _keyName);
        return (CreateOrOpen(_software), DeviceKeyProtection.Software);
    }

    /// <summary>A new key in <paramref name="provider"/>, or the one another start of
    /// the app made there a moment ago.</summary>
    private ECDsa CreateOrOpen(IDeviceKeyProvider provider)
    {
        try
        {
            return provider.Create(_keyName);
        }
        catch (CryptographicException ex) when (CngKeyFailures.ClassifyCreate(ex.HResult) == CngKeyFailure.KeyExists)
        {
            return provider.Open(_keyName);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "This device's key {KeyName} is kept in software, not a TPM; it was made there because no TPM was available.")]
    private static partial void LogKeyKeptInSoftware(ILogger logger, Exception? exception, string keyName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The TPM could not make this device's key {KeyName}, so it is made in software.")]
    private static partial void LogTpmCouldNotMakeKey(ILogger logger, Exception exception, string keyName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "This machine has no usable TPM, so this device's key {KeyName} is made in software.")]
    private static partial void LogNoUsableTpm(ILogger logger, string keyName);
}
