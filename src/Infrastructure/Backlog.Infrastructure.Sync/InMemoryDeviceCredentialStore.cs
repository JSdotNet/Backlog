namespace Backlog.Infrastructure.Sync;

/// <summary>
/// A credential store that forgets on exit.
/// <para>
/// Two hosts want one. The Android head has no DPAPI and its own secure storage
/// adapter is a slice of its own, so until that lands a phone pairs once per
/// run — which is honest about what it can keep rather than writing the secret
/// to a plain file and calling it stored. Tests want one because the thing under
/// test is usually the token provider or a screen, not the envelope.
/// </para>
/// </summary>
public sealed class InMemoryDeviceCredentialStore : IDeviceCredentialStore
{
    public InMemoryDeviceCredentialStore()
    {
    }

    /// <summary>Starts out already paired. For the tests and harnesses that are
    /// about what a paired device does next.</summary>
    public InMemoryDeviceCredentialStore(DeviceCredential? credential) => Current = credential;

    public event Action? Changed;

    public DeviceCredential? Current { get; private set; }

    /// <summary>Not a path at all, and it says so: a settings screen shows this
    /// where it would otherwise show a file name, and "nowhere" is the fact the
    /// reader needs.</summary>
    public string StorePath => "In memory — this device forgets its pairing when the app closes.";

    public void Save(DeviceCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        Current = credential;
        Changed?.Invoke();
    }

    public void Clear()
    {
        Current = null;
        Changed?.Invoke();
    }
}
