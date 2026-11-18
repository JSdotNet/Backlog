using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The device key on the client side (local ADR 0052 §2): the assertion it signs, the
/// stores that keep it, and how registering, pairing and the token exchange use it —
/// including the one-time move of a device on a credential onto its key.
/// </summary>
public sealed class DeviceKeyClientTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Device = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-07T09:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void An_assertion_is_an_ES256_JWT_the_public_key_verifies_with_the_claims_the_service_reads()
    {
        using var keys = new InMemoryDeviceKeyStore();
        var key = keys.GetOrCreate();

        var assertion = DeviceAssertion.Create(Device, key, new FakeTimeProvider(Now));
        var jwt = Jwt.Read(assertion, key.PublicKey);

        Assert.True(jwt.SignatureValid);
        Assert.Equal(SyncAssertion.Algorithm, jwt.Header.GetProperty("alg").GetString());
        Assert.Equal("JWT", jwt.Header.GetProperty("typ").GetString());
        Assert.Equal(Device.ToString("D"), jwt.Claims.GetProperty("iss").GetString());
        Assert.Equal(Device.ToString("D"), jwt.Claims.GetProperty("sub").GetString());
        Assert.Equal(SyncAssertion.Audience, jwt.Claims.GetProperty("aud").GetString());
        Assert.False(string.IsNullOrEmpty(jwt.Claims.GetProperty("jti").GetString()));

        var issuedAt = jwt.Claims.GetProperty("iat").GetInt64();
        var expiresAt = jwt.Claims.GetProperty("exp").GetInt64();
        Assert.Equal(Now.ToUnixTimeSeconds(), issuedAt);
        Assert.Equal(DeviceAssertion.Lifetime.TotalSeconds, expiresAt - issuedAt);
        Assert.True(DeviceAssertion.Lifetime < SyncAssertion.MaxLifetime);
    }

    [Fact]
    public void Every_assertion_carries_its_own_id_so_none_is_a_replay_of_another()
    {
        using var keys = new InMemoryDeviceKeyStore();
        var key = keys.GetOrCreate();
        var clock = new FakeTimeProvider(Now);

        var first = Jwt.Read(DeviceAssertion.Create(Device, key, clock), key.PublicKey).Claims.GetProperty("jti").GetString();
        var second = Jwt.Read(DeviceAssertion.Create(Device, key, clock), key.PublicKey).Claims.GetProperty("jti").GetString();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_signature_from_another_key_does_not_verify()
    {
        using var mine = new InMemoryDeviceKeyStore();
        using var other = new InMemoryDeviceKeyStore();

        var assertion = DeviceAssertion.Create(Device, mine.GetOrCreate(), new FakeTimeProvider(Now));

        Assert.False(Jwt.Read(assertion, other.GetOrCreate().PublicKey).SignatureValid);
    }

    /// <summary>The installed app's store, on the machine the suite runs on. CI has no
    /// TPM, so either protection is accepted; what is asserted is that the key signs,
    /// cannot be exported, and is the same key when opened again.</summary>
    [Fact]
    public void The_CNG_store_creates_a_non_exportable_key_that_signs_and_is_found_again()
    {
        if (!OperatingSystem.IsWindows()) return;

        var name = "Backlog.Sync.DeviceKey.Test." + Guid.NewGuid().ToString("N");

        try
        {
            string publicKey;
            using (var store = new CngDeviceKeyStore(name))
            {
                var key = store.GetOrCreate();
                publicKey = key.PublicKey;

                Assert.Contains(store.Protection, new[] { DeviceKeyProtection.Hardware, DeviceKeyProtection.Software });

                var assertion = DeviceAssertion.Create(Device, key, new FakeTimeProvider(Now));
                Assert.True(Jwt.Read(assertion, publicKey).SignatureValid);
            }

            using (var cng = OpenTestKey(name))
            {
                Assert.Equal(CngExportPolicies.None, cng.ExportPolicy);
            }

            using var reopened = new CngDeviceKeyStore(name);
            Assert.Equal(publicKey, reopened.GetOrCreate().PublicKey);
        }
        finally
        {
            DeleteTestKey(name);
        }
    }

    [Fact]
    public void The_harness_store_keeps_its_key_across_a_restart_and_never_in_the_clear()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), "backlog-device-key-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "device-key.json");

        try
        {
            string publicKey;
            using (var store = new DpapiDeviceKeyStore(path))
            {
                publicKey = store.GetOrCreate().PublicKey;
                Assert.Equal(DeviceKeyProtection.Software, store.Protection);
            }

            using var reopened = new DpapiDeviceKeyStore(path);
            var key = reopened.GetOrCreate();

            Assert.Equal(publicKey, key.PublicKey);
            Assert.True(Jwt.Read(DeviceAssertion.Create(Device, key, new FakeTimeProvider(Now)), publicKey).SignatureValid);
            Assert.DoesNotContain("PRIVATE KEY", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Registering_with_a_key_sends_the_public_key_and_stores_no_secret()
    {
        using var keys = new InMemoryDeviceKeyStore();
        var store = new InMemoryDeviceCredentialStore();
        var bodies = new List<string>();
        using var http = PairingService(bodies, out var client, store, keys);

        var result = await client.RegisterAsync("Workshop PC", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var sent = JsonSerializer.Deserialize<RegisterDeviceRequest>(bodies[^1], JsonSerializerOptions.Web)!;
        Assert.Equal(keys.GetOrCreate().PublicKey, sent.PublicKey);
        Assert.Equal(new DeviceCredential(Owner, Device, "Workshop PC", null), store.Current);
        Assert.True(store.Current!.IsKeyBound);
    }

    [Fact]
    public async Task Pairing_with_a_key_sends_the_public_key_and_stores_no_secret()
    {
        using var keys = new InMemoryDeviceKeyStore();
        var store = new InMemoryDeviceCredentialStore();
        var bodies = new List<string>();
        using var http = PairingService(bodies, out var client, store, keys);

        var result = await client.PairAsync("K7MN9PQR", "Phone", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var sent = JsonSerializer.Deserialize<RedeemPairingCodeRequest>(bodies[^1], JsonSerializerOptions.Web)!;
        Assert.Equal(keys.GetOrCreate().PublicKey, sent.PublicKey);
        Assert.Null(store.Current!.Credential);
    }

    [Fact]
    public async Task Pairing_without_a_key_store_sends_no_key_and_keeps_the_credential()
    {
        var store = new InMemoryDeviceCredentialStore();
        var bodies = new List<string>();
        using var http = PairingService(bodies, out var client, store, keys: null);

        var result = await client.PairAsync("K7MN9PQR", "Phone", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(JsonSerializer.Deserialize<RedeemPairingCodeRequest>(bodies[^1], JsonSerializerOptions.Web)!.PublicKey);
        Assert.Equal("a-registration-credential", store.Current!.Credential);
    }

    [Fact]
    public async Task A_key_bound_device_trades_an_assertion_its_key_signed_for_a_token()
    {
        using var keys = new InMemoryDeviceKeyStore();
        var store = new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, Device, "Workshop PC", null));
        var sent = new List<DeviceTokenRequest>();
        using var fixture = TokenService(store, keys, sent);

        Assert.Equal("token-0", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));

        var request = Assert.Single(sent);
        Assert.Null(request.Credential);
        Assert.Null(request.PublicKey);
        Assert.Equal(SyncAssertion.ClientAssertionType, request.ClientAssertionType);
        Assert.True(Jwt.Read(request.ClientAssertion!, keys.GetOrCreate().PublicKey).SignatureValid);
    }

    [Fact]
    public async Task A_key_bound_device_on_a_host_with_no_key_asks_for_nothing()
    {
        var store = new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, Device, "Workshop PC", null));
        var sent = new List<DeviceTokenRequest>();
        using var fixture = TokenService(store, keys: null, sent);

        Assert.Null(await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Empty(sent);
    }

    /// <summary>The migration: the first fetch sends the secret, the key and an
    /// assertion that key signed, then forgets the secret; the next fetch is an
    /// assertion alone.</summary>
    [Fact]
    public async Task A_device_on_a_credential_moves_onto_its_key_once_and_forgets_the_secret()
    {
        using var keys = new InMemoryDeviceKeyStore();
        var store = new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, Device, "Workshop PC", "a-registration-credential"));
        var sent = new List<DeviceTokenRequest>();
        using var fixture = TokenService(store, keys, sent);

        Assert.Equal("token-0", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));

        var migration = Assert.Single(sent);
        Assert.Equal("a-registration-credential", migration.Credential);
        Assert.Equal(keys.GetOrCreate().PublicKey, migration.PublicKey);
        Assert.Equal(SyncAssertion.ClientAssertionType, migration.ClientAssertionType);
        Assert.True(Jwt.Read(migration.ClientAssertion!, migration.PublicKey!).SignatureValid);

        Assert.Equal(new DeviceCredential(Owner, Device, "Workshop PC", null), store.Current);

        // The token the migration earned is kept, though the save raised Changed.
        Assert.Equal("token-0", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Single(sent);

        fixture.Provider.Invalidate();
        Assert.Equal("token-1", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));

        var next = sent[^1];
        Assert.Null(next.Credential);
        Assert.Null(next.PublicKey);
        Assert.True(Jwt.Read(next.ClientAssertion!, keys.GetOrCreate().PublicKey).SignatureValid);
    }

    /// <summary>A migration the service already took on an earlier run, whose secret this
    /// machine failed to forget: the refusal is settled by one assertion, and the secret
    /// is forgotten then.</summary>
    [Fact]
    public async Task A_refused_migration_is_retried_once_with_the_key_alone()
    {
        using var keys = new InMemoryDeviceKeyStore();
        var store = new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, Device, "Workshop PC", "a-registration-credential"));
        var sent = new List<DeviceTokenRequest>();
        using var fixture = TokenService(
            store, keys, sent, request => request.Credential is null ? null : HttpStatusCode.Unauthorized);

        Assert.Equal("token-1", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));

        Assert.Equal(2, sent.Count);
        Assert.Null(sent[1].Credential);
        Assert.True(Jwt.Read(sent[1].ClientAssertion!, keys.GetOrCreate().PublicKey).SignatureValid);
        Assert.Null(store.Current!.Credential);
        Assert.False(fixture.Provider.CredentialRejected);
    }

    [Fact]
    public async Task A_refused_assertion_is_a_rejected_credential()
    {
        using var keys = new InMemoryDeviceKeyStore();
        var store = new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, Device, "Workshop PC", null));
        var sent = new List<DeviceTokenRequest>();
        using var fixture = TokenService(store, keys, sent, _ => HttpStatusCode.Unauthorized);

        Assert.Null(await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));

        Assert.Single(sent);
        Assert.True(fixture.Provider.CredentialRejected);
    }

    [Fact]
    public async Task A_host_with_no_key_store_goes_on_sending_the_secret()
    {
        var store = new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, Device, "Phone", "a-registration-credential"));
        var sent = new List<DeviceTokenRequest>();
        using var fixture = TokenService(store, keys: null, sent);

        Assert.Equal("token-0", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));

        var request = Assert.Single(sent);
        Assert.Equal("a-registration-credential", request.Credential);
        Assert.Null(request.PublicKey);
        Assert.Null(request.ClientAssertion);
        Assert.Equal("a-registration-credential", store.Current!.Credential);
    }

    [Fact]
    public void A_device_with_no_secret_survives_a_restart_of_the_DPAPI_store()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), "backlog-device-key-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "device-credential.json");

        try
        {
            var credential = new DeviceCredential(Owner, Device, "Workshop PC", null);
            new DpapiDeviceCredentialStore(path).Save(credential);

            Assert.Equal(credential, new DpapiDeviceCredentialStore(path).Current);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_device_with_no_secret_survives_a_restart_of_the_secure_storage_store()
    {
        var platform = new DictionarySecureValues();
        var credential = new DeviceCredential(Owner, Device, "Pixel", null);

        await new SecureValueDeviceCredentialStore(platform, "test").SaveAsync(credential, TestContext.Current.CancellationToken);

        Assert.Equal(credential, new SecureValueDeviceCredentialStore(platform, "test").Current);
    }

    private static HttpClient PairingService(
        List<string> bodies, out DevicePairingClient client, IDeviceCredentialStore store, IDeviceKeyStore? keys)
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var body = request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            bodies.Add(body);

            if (request.RequestUri!.AbsolutePath == SyncRoutes.Absolute(SyncRoutes.EnrolmentStart))
            {
                var start = JsonSerializer.Deserialize<StartEnrolmentRequest>(body, JsonSerializerOptions.Web)!;
                var address = new Uri($"{start.RedirectUri.AbsoluteUri}?code=one-time-code&state={Uri.EscapeDataString(start.State)}");
                return StubHttpMessageHandler.Json(
                    HttpStatusCode.OK, JsonSerializer.Serialize(new EnrolmentStartResponse(address), JsonSerializerOptions.Web));
            }

            // The service as slice A built it: a key gets no credential.
            using var sentBody = JsonDocument.Parse(body);
            var keyed = sentBody.RootElement.TryGetProperty("publicKey", out var key) && key.ValueKind == JsonValueKind.String;
            var credential = keyed ? null : "a-registration-credential";
            return StubHttpMessageHandler.Json(
                HttpStatusCode.Created,
                JsonSerializer.Serialize(new DeviceRegistrationResponse(Owner, Device, credential), JsonSerializerOptions.Web));
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };
        client = new DevicePairingClient(http, store, browser: new NoBrowser(), keys: keys);
        return http;
    }

    /// <summary>The token endpoint, recording each request it is sent; <paramref name="refuse"/>
    /// names the status to answer a request with instead of a token, or null to mint one.</summary>
    private static SyncTokenProviderTests.Fixture TokenService(
        IDeviceCredentialStore store,
        IDeviceKeyStore? keys,
        List<DeviceTokenRequest> sent,
        Func<DeviceTokenRequest, HttpStatusCode?>? refuse = null)
    {
        var clock = new FakeTimeProvider(Now);

        return SyncTokenProviderTests.Fixture.Create(
            store,
            (request, index) =>
            {
                var body = request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
                var token = JsonSerializer.Deserialize<DeviceTokenRequest>(body, JsonSerializerOptions.Web)!;
                sent.Add(token);

                return refuse?.Invoke(token) is { } status
                    ? new HttpResponseMessage(status)
                    : SyncTokenProviderTests.Fixture.TokenResponse(index, clock);
            },
            clock: clock,
            keys: keys);
    }

    private static CngKey OpenTestKey(string name)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();

        return CngKey.Exists(name, CngProvider.MicrosoftPlatformCryptoProvider)
            ? CngKey.Open(name, CngProvider.MicrosoftPlatformCryptoProvider)
            : CngKey.Open(name, CngProvider.MicrosoftSoftwareKeyStorageProvider);
    }

    private static void DeleteTestKey(string name)
    {
        if (!OperatingSystem.IsWindows()) return;

        foreach (var provider in new[] { CngProvider.MicrosoftPlatformCryptoProvider, CngProvider.MicrosoftSoftwareKeyStorageProvider })
        {
            try
            {
                if (CngKey.Exists(name, provider))
                {
                    using var key = CngKey.Open(name, provider);
                    key.Delete();
                }
            }
            catch (CryptographicException)
            {
                // The provider is not available here, so there is nothing in it to delete.
            }
        }
    }

    private sealed class NoBrowser : ISystemBrowser
    {
        public Task OpenAsync(Uri address, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The Development service answers straight to the loopback.");
    }

    private sealed class DictionarySecureValues : ISecureValueStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key) => Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(string key, string value)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public bool Remove(string key) => _values.Remove(key);
    }

    /// <summary>A compact JWT read back the way the service reads it.</summary>
    private sealed record Jwt(JsonElement Header, JsonElement Claims, bool SignatureValid)
    {
        public static Jwt Read(string compact, string publicKey)
        {
            var parts = compact.Split('.');
            Assert.Equal(3, parts.Length);

            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Base64Url.DecodeFromChars(publicKey), out _);

            var valid = key.VerifyData(
                Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]),
                Base64Url.DecodeFromChars(parts[2]),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

            return new Jwt(
                JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0])).RootElement.Clone(),
                JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1])).RootElement.Clone(),
                valid);
        }
    }
}
