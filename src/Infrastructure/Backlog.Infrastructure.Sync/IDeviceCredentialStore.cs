namespace Backlog.Infrastructure.Sync;

/// <summary>
/// Where this device keeps its registration credential between runs.
/// <para>
/// Shaped on <c>AppFeatureSettingsStore</c> in the file-system adapter, and for
/// the same reasons: the current value is read once when the store is
/// constructed rather than on every access, and <see cref="Changed"/> is what
/// lets everything holding a derived value — the token provider's cache, a
/// settings screen — find out that the answer moved.
/// </para>
/// <para>
/// The port says nothing about how the secret is protected. That is the whole
/// point of it being a port: Windows gets DPAPI, the Android head gets an
/// in-memory store until its SecureStorage adapter lands, and a test gets
/// whichever of the two it can assert against.
/// </para>
/// </summary>
public interface IDeviceCredentialStore
{
    /// <summary>The stored credential, or <c>null</c> when this device has never
    /// been registered or paired — which is also what a store answers when the
    /// file it reads turns out to be unreadable.</summary>
    DeviceCredential? Current { get; }

    /// <summary>Records the credential, replacing anything already stored, and
    /// raises <see cref="Changed"/>. A store that cannot record it throws, and
    /// leaves <see cref="Current"/> alone and <see cref="Changed"/> unraised —
    /// a device that half-paired is a state nothing on screen could explain.</summary>
    void Save(DeviceCredential credential);

    /// <summary>Forgets the credential, and raises <see cref="Changed"/>. The
    /// device is unpaired afterwards as far as this head is concerned; the
    /// service still has its record.</summary>
    void Clear();

    /// <summary>Where the credential is kept, for a settings screen to show.
    /// Never the credential itself.</summary>
    string StorePath { get; }

    /// <summary>Raised after <see cref="Save"/> and <see cref="Clear"/>.</summary>
    event Action? Changed;
}
