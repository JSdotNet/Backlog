namespace Backlog.Infrastructure.Sync;

/// <summary>
/// How a development host picks a device key store, on
/// <see cref="DeviceCredentialStoreFactory"/>'s terms: an overridable path beside the
/// credential so the key lives exactly as long as the credential bound to it, and a
/// process-lifetime fallback where DPAPI does not exist. Development only — the
/// Windows head registers <see cref="CngDeviceKeyStore"/>.
/// </summary>
public static class DeviceKeyStoreFactory
{
    /// <summary>A store for a locally run host, at the path
    /// <paramref name="environmentVariableName"/> names or, failing that,
    /// <paramref name="defaultRelativePath"/> under <paramref name="contentRootPath"/>.</summary>
    public static IDeviceKeyStore CreateLocalDevelopmentStore(
        string contentRootPath,
        string environmentVariableName,
        string defaultRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultRelativePath);

        var keyPath = Environment.GetEnvironmentVariable(environmentVariableName);
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            keyPath = Path.Combine(contentRootPath, defaultRelativePath);
        }

        return OperatingSystem.IsWindows()
            ? new DpapiDeviceKeyStore(keyPath)
            : new InMemoryDeviceKeyStore();
    }
}
