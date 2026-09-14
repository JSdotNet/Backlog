namespace Backlog.Infrastructure.Sync;

/// <summary>
/// A platform's own encrypted key-value store, seen through the three calls the
/// credential store needs. The shape is MAUI's <c>ISecureStorage</c> — asynchronous
/// reads and writes, a synchronous remove — so the Android head's adapter is a
/// forwarding shim and nothing more.
/// <para>
/// A port rather than a reference to MAUI Essentials because this project is
/// TFM-neutral and its tests run in the ordinary suite. The head is the only
/// place a real implementation exists; a test hands
/// <see cref="SecureValueDeviceCredentialStore"/> a dictionary.
/// </para>
/// </summary>
public interface ISecureValueStore
{
    /// <summary>The value under <paramref name="key"/>, or <c>null</c> when
    /// nothing is stored there. May throw when the platform cannot open its
    /// store at all.</summary>
    Task<string?> GetAsync(string key);

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/>,
    /// replacing what was there. Throws when the platform cannot keep it.</summary>
    Task SetAsync(string key, string value);

    /// <summary>Forgets <paramref name="key"/>; <c>true</c> when there was
    /// something to forget.</summary>
    bool Remove(string key);
}
