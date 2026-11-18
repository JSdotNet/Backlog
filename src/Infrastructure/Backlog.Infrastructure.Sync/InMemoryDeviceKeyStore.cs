using System.Security.Cryptography;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// A software P-256 key that lives as long as the process: for tests, and for a
/// development host on a platform without DPAPI. It reports
/// <see cref="DeviceKeyProtection.Software"/>, because that is what it is.
/// </summary>
public sealed class InMemoryDeviceKeyStore : IDeviceKeyStore, IDisposable
{
    private readonly EcdsaDeviceKey _key = new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    public DeviceKeyProtection Protection => DeviceKeyProtection.Software;

    public IDeviceKey GetOrCreate() => _key;

    public void Dispose() => _key.Dispose();
}
