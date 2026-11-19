namespace Backlog.Infrastructure.Sync;

/// <summary>
/// Where this device's signing key lives (local ADR 0058 §2). The port says nothing
/// about how it is protected beyond <see cref="Protection"/>: the Windows head keeps
/// it in CNG, TPM first; the Android head in Android Keystore, StrongBox first; a
/// development harness keeps a software one. A host with no store registers none, and
/// its device stays on a registration credential.
/// </summary>
public interface IDeviceKeyStore
{
    /// <summary>The key, opened if this device already has one and created
    /// otherwise. Throws <see cref="System.Security.Cryptography.CryptographicException"/>
    /// when neither can be done.</summary>
    IDeviceKey GetOrCreate();

    /// <summary>How the key is kept. Opens or creates it to find out.</summary>
    DeviceKeyProtection Protection { get; }
}
