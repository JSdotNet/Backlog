using Backlog.Infrastructure.Sync;

using Microsoft.Maui.Storage;

namespace Backlog.Mobile.Services;

/// <summary>
/// <see cref="IDeviceCredentialStore"/> over MAUI's <see cref="ISecureStorage"/>
/// — on Android, <c>EncryptedSharedPreferences</c> under a Keystore master key.
/// The answer to "the OS credential store" ADR 0005 names, for the platform
/// that has no DPAPI.
/// </summary>
/// <remarks>
/// <para>
/// This class is the Android head's contribution and nothing else: the shim from
/// <see cref="ISecureStorage"/> to <see cref="ISecureValueStore"/>, and the
/// label a settings screen shows. Every decision worth a test — the one key, the
/// JSON, the cache behind the synchronous <see cref="Current"/>, what an
/// unreadable value turns into, and why a failed write surfaces as an
/// <see cref="IOException"/> — lives in <see cref="SecureValueDeviceCredentialStore"/>,
/// where the ordinary suite can reach it. The seam is chosen there too: one
/// blocking read in the constructor, which is why <c>MauiProgram</c> resolves
/// this store right after <c>Build()</c> rather than leaving the first
/// resolution to the Inbox's render.
/// </para>
/// <para>
/// <see cref="ISecureStorage"/> is injected rather than read off
/// <see cref="SecureStorage.Default"/> so that the composition is visible in
/// <c>MauiProgram</c> beside the other Android-only adapters.
/// </para>
/// <para>
/// Uninstalling the app clears the Keystore entry along with the preferences,
/// so a fresh install starts unpaired — which is the right answer, since the
/// service still holds the old device's record and the pairing screen is how a
/// new one is made. There is no un-pair control on the phone today; reinstall is
/// the only forget path.
/// </para>
/// </remarks>
public sealed class SecureStorageDeviceCredentialStore : IDeviceCredentialStore
{
    /// <summary>User-facing copy: where the credential is kept, for the same
    /// slot a desktop settings screen shows a file path in.</summary>
    private const string Label = "Android secure storage (Keystore)";

    private readonly SecureValueDeviceCredentialStore _inner;

    public SecureStorageDeviceCredentialStore(ISecureStorage secureStorage)
    {
        ArgumentNullException.ThrowIfNull(secureStorage);

        _inner = new SecureValueDeviceCredentialStore(new MauiSecureValueStore(secureStorage), Label);
    }

    /// <inheritdoc />
    public event Action? Changed
    {
        add => _inner.Changed += value;
        remove => _inner.Changed -= value;
    }

    /// <inheritdoc />
    public DeviceCredential? Current => _inner.Current;

    /// <inheritdoc />
    public string StorePath => _inner.StorePath;

    /// <inheritdoc />
    public void Save(DeviceCredential credential) => _inner.Save(credential);

    /// <inheritdoc />
    public void Clear() => _inner.Clear();

    /// <summary>The forwarding shim. The two interfaces have the same shape on
    /// purpose, so there is nothing to translate.</summary>
    private sealed class MauiSecureValueStore(ISecureStorage secureStorage) : ISecureValueStore
    {
        public Task<string?> GetAsync(string key) => secureStorage.GetAsync(key);

        public Task SetAsync(string key, string value) => secureStorage.SetAsync(key, value);

        public bool Remove(string key) => secureStorage.Remove(key);
    }
}
