using System.Text.Json;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The platform-neutral half of the Android credential store. The head wraps
/// MAUI's <c>ISecureStorage</c> in an <see cref="ISecureValueStore"/> and hands
/// it here, so everything that can be asserted without a device — the cache,
/// the JSON, the <c>Changed</c> discipline, and what an unreadable value turns
/// into — is asserted here against a dictionary.
/// <para>
/// What these tests cannot say is that the value is encrypted at rest: that is
/// the platform's promise, checked on the emulator with <c>adb shell run-as</c>.
/// </para>
/// </summary>
public sealed class SecureValueDeviceCredentialStoreTests
{
    private const string Label = "Android secure storage (Keystore)";

    private static readonly DeviceCredential Paired = new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Pixel",
        "Zm9yLXRoZS1zYWtlLW9mLXRoaXMtdGVzdC1vbmx5LXNlY3JldA");

    [Fact]
    public void The_credential_survives_a_restart()
    {
        var secure = new FakeSecureValueStore();

        new SecureValueDeviceCredentialStore(secure, Label).Save(Paired);

        // A second store over the same secure storage is what a force-stop and
        // relaunch looks like: nothing in memory, only what the platform kept.
        Assert.Equal(Paired, new SecureValueDeviceCredentialStore(secure, Label).Current);
    }

    [Fact]
    public void It_is_correct_before_anybody_asks()
    {
        var secure = new FakeSecureValueStore();
        secure.Values[SecureValueDeviceCredentialStore.Key] = JsonSerializer.Serialize(
            Paired, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var store = new SecureValueDeviceCredentialStore(secure, Label);

        // Read once, in the constructor, so the Inbox's first synchronous look at
        // Current is the answer rather than a pairing box that flashes and goes.
        Assert.Equal(Paired, store.Current);
        Assert.Equal(1, secure.Reads);
    }

    [Fact]
    public void Everything_is_kept_under_one_key()
    {
        var secure = new FakeSecureValueStore();

        new SecureValueDeviceCredentialStore(secure, Label).Save(Paired);

        var (key, value) = Assert.Single(secure.Values);
        Assert.Equal(SecureValueDeviceCredentialStore.Key, key);
        Assert.Contains(Paired.Credential, value);
        Assert.Contains(Paired.OwnerId.ToString("D"), value);
    }

    [Fact]
    public void Clearing_forgets_it_here_and_in_secure_storage()
    {
        var secure = new FakeSecureValueStore();
        var store = new SecureValueDeviceCredentialStore(secure, Label);
        store.Save(Paired);

        store.Clear();

        Assert.Null(store.Current);
        Assert.Empty(secure.Values);
        Assert.Null(new SecureValueDeviceCredentialStore(secure, Label).Current);
    }

    [Fact]
    public void Saving_and_clearing_both_say_so()
    {
        var store = new SecureValueDeviceCredentialStore(new FakeSecureValueStore(), Label);
        var changes = 0;
        store.Changed += () => changes++;

        store.Save(Paired);
        store.Clear();

        Assert.Equal(2, changes);
    }

    /// <summary>
    /// A value this device cannot read — a half-written one, one from an earlier
    /// shape, a decryption the Keystore refused — is "not paired", and the key
    /// goes with it so the next launch does not trip over the same value again.
    /// Pairing again is the only recovery, and the unpaired screen offers it.
    /// </summary>
    [Theory]
    [InlineData("this is not json")]
    [InlineData("{\"ownerId\":\"not-a-guid\"}")]
    [InlineData("{}")]
    [InlineData("{\"ownerId\":\"6b8f4a0e-5f7a-4c33-9f1d-3f0a3b2c1d00\",\"deviceId\":\"6b8f4a0e-5f7a-4c33-9f1d-3f0a3b2c1d01\",\"deviceName\":\"Pixel\",\"credential\":\"\"}")]
    [InlineData("")]
    public void A_value_that_cannot_be_read_is_not_paired_and_is_removed(string stored)
    {
        var secure = new FakeSecureValueStore();
        secure.Values[SecureValueDeviceCredentialStore.Key] = stored;

        var store = new SecureValueDeviceCredentialStore(secure, Label);

        Assert.Null(store.Current);
        Assert.Empty(secure.Values);
    }

    [Fact]
    public void A_read_the_platform_refuses_is_not_paired_rather_than_a_crash_on_launch()
    {
        var secure = new FakeSecureValueStore { ReadFails = true };

        var store = new SecureValueDeviceCredentialStore(secure, Label);

        Assert.Null(store.Current);
    }

    /// <summary>
    /// The pairing client catches <see cref="IOException"/> from
    /// <see cref="IDeviceCredentialStore.Save"/> and turns it into "could not be
    /// saved on this machine". Whatever the platform threw — and on Android it is
    /// a Java exception this project cannot name — has to arrive there as one.
    /// </summary>
    [Fact]
    public void A_credential_that_cannot_be_written_is_not_kept_and_nothing_says_it_changed()
    {
        var store = new SecureValueDeviceCredentialStore(new FakeSecureValueStore { WriteFails = true }, Label);
        var changes = 0;
        store.Changed += () => changes++;

        var thrown = Record.Exception(() => store.Save(Paired));

        var io = Assert.IsType<IOException>(thrown);
        Assert.NotNull(io.InnerException);
        Assert.Null(store.Current);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void An_empty_store_starts_unpaired_and_says_where_it_keeps_things()
    {
        var store = new SecureValueDeviceCredentialStore(new FakeSecureValueStore(), Label);

        Assert.Null(store.Current);
        Assert.Equal(Label, store.StorePath);
    }

    /// <summary>Secure storage as a dictionary, with the two ways the real one
    /// can fail: on the read at launch, and on the write during pairing.</summary>
    private sealed class FakeSecureValueStore : ISecureValueStore
    {
        public Dictionary<string, string> Values { get; } = new();

        public int Reads { get; private set; }

        public bool ReadFails { get; init; }

        public bool WriteFails { get; init; }

        public Task<string?> GetAsync(string key)
        {
            Reads++;
            if (ReadFails) throw new InvalidOperationException("Keystore refused.");

            return Task.FromResult(Values.TryGetValue(key, out var value) ? value : null);
        }

        public Task SetAsync(string key, string value)
        {
            if (WriteFails) throw new InvalidOperationException("Keystore refused.");

            Values[key] = value;
            return Task.CompletedTask;
        }

        public bool Remove(string key) => Values.Remove(key);
    }
}
