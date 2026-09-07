namespace Backlog.Infrastructure.Sync;

/// <summary>
/// How a development host picks a credential store.
/// <para>
/// The desktop and mobile web harnesses both need one, and both need the same
/// two decisions: an overridable path so a session can point a harness somewhere
/// of its own, and a fallback for the platforms DPAPI does not exist on. Written
/// twice it was the same twenty lines in two files, which is one file too many
/// for a rule that has to stay the same on both — the harnesses are a pair on
/// purpose, and a fallback that drifted between them would make the pairing flow
/// behave differently depending on which side started it.
/// </para>
/// <para>
/// Development only. The shipped heads register their own store: the desktop
/// head takes DPAPI directly, and the Android head takes the in-memory store
/// until its SecureStorage adapter lands.
/// </para>
/// </summary>
public static class DeviceCredentialStoreFactory
{
    /// <summary>
    /// A store for a locally run host, at the path
    /// <paramref name="environmentVariableName"/> names or, failing that, under
    /// the host's own content root.
    /// </summary>
    /// <param name="contentRootPath">The host's content root, which is what
    /// makes two harnesses on one machine two devices rather than one seen
    /// twice.</param>
    /// <param name="environmentVariableName">The variable a session sets to move
    /// the credential elsewhere. Each harness has its own, so pointing one of
    /// them somewhere does not silently move the other onto the same file and
    /// collapse the pair back into a single device.</param>
    /// <param name="defaultRelativePath">Where the credential goes when that
    /// variable is unset, relative to <paramref name="contentRootPath"/>.</param>
    public static IDeviceCredentialStore CreateLocalDevelopmentStore(
        string contentRootPath,
        string environmentVariableName,
        string defaultRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultRelativePath);

        var credentialPath = Environment.GetEnvironmentVariable(environmentVariableName);
        if (string.IsNullOrWhiteSpace(credentialPath))
        {
            credentialPath = Path.Combine(contentRootPath, defaultRelativePath);
        }

        // DPAPI is the Windows half of the port and these harnesses are normally
        // run on Windows. Somewhere else the credential is kept for the life of
        // the process instead, which costs a re-pair per run and never writes a
        // secret in the clear — the one thing that would not be acceptable
        // either way.
        return OperatingSystem.IsWindows()
            ? new DpapiDeviceCredentialStore(credentialPath)
            : new InMemoryDeviceCredentialStore();
    }
}
