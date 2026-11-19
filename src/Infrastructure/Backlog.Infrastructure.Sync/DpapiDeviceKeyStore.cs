using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// A software P-256 device key kept in a file under a DPAPI envelope, for the
/// development harnesses: their credential file outlives a restart, so their key has
/// to as well, or every restart would leave a device bound to a key it no longer
/// holds. Never the installed app's store — that is <see cref="CngDeviceKeyStore"/>,
/// whose key cannot be exported at all. This one reports
/// <see cref="DeviceKeyProtection.Software"/>.
/// <para>
/// A file this user cannot open is treated as no key: a new one is made and written
/// over it, and the device it was bound to is registered again, as with an unreadable
/// credential.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiDeviceKeyStore : IDeviceKeyStore, IDisposable
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private EcdsaDeviceKey? _key;

    public DpapiDeviceKeyStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public DeviceKeyProtection Protection => DeviceKeyProtection.Software;

    public IDeviceKey GetOrCreate()
    {
        lock (_gate)
        {
            return _key ??= new EcdsaDeviceKey(Read() ?? Create());
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

    private ECDsa? Read()
    {
        byte[]? pkcs8 = null;
        var key = ECDsa.Create();

        try
        {
            if (!File.Exists(_path))
            {
                key.Dispose();
                return null;
            }

            pkcs8 = UserDpapi.Open(File.ReadAllText(_path));
            key.ImportPkcs8PrivateKey(pkcs8, out _);
            return key;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or CryptographicException)
        {
            key.Dispose();
            return null;
        }
        finally
        {
            if (pkcs8 is not null) Array.Clear(pkcs8);
        }
    }

    private ECDsa Create()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pkcs8 = key.ExportPkcs8PrivateKey();
        var temporary = _path + ".tmp";

        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            // Written beside the file and moved over it, so a crash mid-write leaves the
            // old key or the new one, never half of either.
            File.WriteAllText(temporary, UserDpapi.Seal(pkcs8));
            File.Move(temporary, _path, overwrite: true);
            return key;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            key.Dispose();
            DeleteBestEffort(temporary);
            throw new CryptographicException("The device key could not be kept on this machine.", ex);
        }
        finally
        {
            Array.Clear(pkcs8);
        }
    }

    /// <summary>Removes a half-written envelope. Best effort: what stopped the write may
    /// stop this too, and the failure being reported is the write's.</summary>
    private static void DeleteBestEffort(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind; the next create writes over it.
        }
    }
}
