using System.Text.Json;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The credential store over a platform's encrypted key-value store — on
/// Android, the Keystore-wrapped preferences MAUI's <c>SecureStorage</c> fronts.
/// This is the half that can be tested without a device: the JSON, the cache,
/// the <see cref="Changed"/> discipline, and what an unreadable value turns into.
/// The head contributes only the <see cref="ISecureValueStore"/> adapter.
/// <para>
/// The whole <see cref="DeviceCredential"/> goes under one key as camelCase JSON,
/// the same shape <see cref="DpapiDeviceCredentialStore"/> seals inside its
/// envelope. Nothing moves a credential between platforms, so that is a
/// convenience for anyone reading both, not a contract.
/// </para>
/// <para>
/// <b>The sync/async seam.</b> The platform store is asynchronous and
/// <see cref="IDeviceCredentialStore.Current"/> is not: the Inbox reads it
/// synchronously on every render to decide whether to show the pairing box. So
/// the value is read exactly once, here in the constructor, blocking on the
/// platform's task, and served from the cache afterwards. Blocking is the
/// simpler of the two choices and it is safe: MAUI's Android implementation runs
/// the read on the thread pool rather than on the caller's dispatcher, the value
/// is a few hundred bytes, and the head already pays comparable startup costs.
/// The alternative — an <c>InitializeAsync</c> the head awaits after
/// <c>Build()</c> — would add a second way to construct the store for a wait
/// nobody would notice. The head still resolves the store eagerly after
/// <c>Build()</c>, so the one blocking read happens at startup rather than
/// under the Inbox's first render.
/// </para>
/// <para>
/// A stored value this device cannot read — a half-written one, one from an
/// earlier shape, a decryption the Keystore refused after a reinstall or a
/// restored backup — is "not paired", and the key is removed so the next launch
/// does not trip over it again. Pairing again is the only recovery, and it is
/// what the unpaired screen already offers; nothing here logs, for the reason
/// the DPAPI store gives.
/// </para>
/// </summary>
public sealed class SecureValueDeviceCredentialStore : IDeviceCredentialStore
{
    /// <summary>The one key. Namespaced so a second secret this head ever keeps
    /// cannot collide with it.</summary>
    internal const string Key = "backlog.sync.device-credential";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ISecureValueStore _store;

    /// <param name="store">The platform's encrypted store.</param>
    /// <param name="storePath">What a settings screen shows as the location —
    /// a label such as "Android secure storage (Keystore)", never a path,
    /// because there is no path a person could do anything with.</param>
    public SecureValueDeviceCredentialStore(ISecureValueStore store, string storePath)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);

        _store = store;
        StorePath = storePath;
        Current = Read();
    }

    public event Action? Changed;

    public DeviceCredential? Current { get; private set; }

    public string StorePath { get; }

    /// <summary>
    /// Writes the credential, then adopts it, in that order for the reason the
    /// DPAPI store gives: a write that fails throws and leaves
    /// <see cref="Current"/> and <see cref="Changed"/> exactly as they were.
    /// Whatever the platform threw arrives at the pairing client as an
    /// <see cref="IOException"/>, because that is what it catches and turns
    /// into "could not be saved on this machine" — on Android the original is a
    /// Java exception this project cannot name, and letting it through would
    /// be an unhandled exception on the pairing screen instead of a message.
    /// </summary>
    public void Save(DeviceCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        try
        {
            _store.SetAsync(Key, JsonSerializer.Serialize(credential, JsonOptions)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not IOException)
        {
            throw new IOException("The device credential could not be written to secure storage.", ex);
        }

        Current = credential;
        Changed?.Invoke();
    }

    public void Clear()
    {
        Current = null;

        Forget();

        Changed?.Invoke();
    }

    private DeviceCredential? Read()
    {
        string? stored;

        try
        {
            stored = _store.GetAsync(Key).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // The platform boundary. Whatever could not be opened — the Keystore
            // entry, the preferences file — is not something this head can
            // repair from here, and the screen that reads Current already knows
            // what to offer when it is null.
            return null;
        }

        if (stored is null) return null;

        DeviceCredential? credential;
        try
        {
            credential = JsonSerializer.Deserialize<DeviceCredential>(stored, JsonOptions);
        }
        catch (JsonException)
        {
            credential = null;
        }

        if (credential is null || string.IsNullOrWhiteSpace(credential.Credential))
        {
            Forget();
            return null;
        }

        return credential;
    }

    private void Forget()
    {
        try
        {
            _store.Remove(Key);
        }
        catch (Exception)
        {
            // A key that cannot be removed is already unreadable or already
            // gone; either way the next read answers null, which is the state
            // being asked for.
        }
    }
}
