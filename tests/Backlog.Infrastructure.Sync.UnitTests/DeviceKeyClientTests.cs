using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Logging;
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

    /// <summary>Only a provider that is not there, or a TPM that is absent or switched
    /// off, sends the key to software; a transient failure — a TPM in lockout, a denied
    /// access — is thrown, so a TPM key that exists is never shadowed by a software one
    /// under the same name.</summary>
    [Theory]
    [InlineData(0x8009001EU)] // NTE_PROV_DLL_NOT_FOUND
    [InlineData(0x80090029U)] // NTE_NOT_SUPPORTED
    [InlineData(0x80090030U)] // NTE_DEVICE_NOT_READY
    [InlineData(0x80090035U)] // NTE_DEVICE_NOT_FOUND
    [InlineData(0x80284008U)] // TBS_E_SERVICE_NOT_RUNNING
    [InlineData(0x8028400FU)] // TBS_E_TPM_NOT_FOUND
    [InlineData(0x80284010U)] // TBS_E_SERVICE_DISABLED
    [InlineData(0x80280006U)] // TPM_E_DISABLED
    [InlineData(0x80280007U)] // TPM_E_DEACTIVATED
    [InlineData(0x80290101U)] // TPM_E_PCP_DEVICE_NOT_READY
    public void A_missing_or_switched_off_TPM_is_the_one_reason_to_fall_back_to_software(uint hResult)
    {
        Assert.Equal(CngKeyFailure.PlatformUnavailable, CngKeyFailures.Classify(unchecked((int)hResult)));
    }

    [Theory]
    [InlineData(0x80280921U)] // TPM_20_E_LOCKOUT
    [InlineData(0x80280803U)] // TPM_E_DEFEND_LOCK_RUNNING
    [InlineData(0x80070005U)] // E_ACCESSDENIED
    [InlineData(0x80090016U)] // NTE_BAD_KEYSET
    [InlineData(0x80284001U)] // TBS_E_INTERNAL_ERROR
    [InlineData(0x8009000DU)] // NTE_NO_KEY
    [InlineData(0x00000000U)]
    public void A_transient_or_unknown_failure_is_never_a_reason_to_fall_back(uint hResult)
    {
        Assert.Equal(CngKeyFailure.Other, CngKeyFailures.Classify(unchecked((int)hResult)));
    }

    [Fact]
    public void A_key_that_already_exists_is_opened_rather_than_made_again()
    {
        Assert.Equal(CngKeyFailure.KeyExists, CngKeyFailures.Classify(unchecked((int)0x8009000FU))); // NTE_EXISTS
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

    /// <summary>A key that cannot be written leaves no half-written envelope beside the
    /// file it was meant to replace.</summary>
    [Fact]
    public void The_harness_store_leaves_no_temporary_file_when_its_key_cannot_be_kept()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), "backlog-device-key-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "device-key.json");

        try
        {
            // A folder where the file goes: the envelope is written beside it, and the
            // move over it fails.
            Directory.CreateDirectory(path);
            using var store = new DpapiDeviceKeyStore(path);

            Assert.Throws<CryptographicException>(store.GetOrCreate);
            Assert.False(File.Exists(path + ".tmp"));
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
        Assert.True(fixture.Provider.KeyUnavailable);
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

    /// <summary>The service took the migration but this machine could not write the
    /// record without its secret: the token is still good and is used, and the next
    /// fetch's re-sent migration — which the service answers on its assertion, ignoring
    /// the secret beside it — forgets the secret then.</summary>
    [Fact]
    public async Task A_secret_that_cannot_be_forgotten_keeps_the_token_and_is_forgotten_on_the_next_fetch()
    {
        using var keys = new InMemoryDeviceKeyStore();
        var store = new UnwritableCredentialStore(new DeviceCredential(Owner, Device, "Workshop PC", "a-registration-credential"));
        var sent = new List<DeviceTokenRequest>();
        using var fixture = TokenService(store, keys, sent);

        Assert.Equal("token-0", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal("a-registration-credential", store.Current!.Credential);
        Assert.Equal("token-0", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Single(sent);

        store.Writable = true;
        fixture.Provider.Invalidate();

        Assert.Equal("token-1", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal("a-registration-credential", sent[^1].Credential);
        Assert.NotNull(sent[^1].ClientAssertion);
        Assert.Null(store.Current!.Credential);
    }

    /// <summary>A device bound to its key whose key cannot be opened cannot prove itself
    /// at all: that is said, rather than the device going quiet.</summary>
    [Fact]
    public async Task A_key_bound_device_whose_key_cannot_be_opened_reports_its_key_unavailable()
    {
        var keys = new UnreliableKeyStore { OpenFails = true };
        var store = new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, Device, "Workshop PC", null));
        var sent = new List<DeviceTokenRequest>();
        using var fixture = TokenService(store, keys, sent);
        var raised = 0;
        fixture.Provider.KeyUnavailableChanged += () => raised++;

        Assert.Null(await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));

        Assert.Empty(sent);
        Assert.True(fixture.Provider.KeyUnavailable);
        Assert.False(fixture.Provider.CredentialRejected);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task A_key_bound_device_whose_key_will_not_sign_reports_its_key_unavailable()
    {
        var keys = new UnreliableKeyStore { SignFails = true };
        var store = new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, Device, "Workshop PC", null));
        var sent = new List<DeviceTokenRequest>();
        using var fixture = TokenService(store, keys, sent);

        Assert.Null(await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));

        Assert.Empty(sent);
        Assert.True(fixture.Provider.KeyUnavailable);
    }

    /// <summary>A key that comes back — a TPM out of lockout — is not remembered as gone:
    /// the next fetch asks the store again and the state clears with the token.</summary>
    [Fact]
    public async Task A_key_that_comes_back_clears_the_unavailable_state()
    {
        var keys = new UnreliableKeyStore { OpenFails = true };
        var store = new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, Device, "Workshop PC", null));
        var sent = new List<DeviceTokenRequest>();
        using var fixture = TokenService(store, keys, sent);

        Assert.Null(await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        keys.OpenFails = false;

        Assert.Equal("token-0", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.False(fixture.Provider.KeyUnavailable);
    }

    [Fact]
    public async Task Forgetting_the_device_clears_the_unavailable_state()
    {
        var keys = new UnreliableKeyStore { OpenFails = true };
        var store = new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, Device, "Workshop PC", null));
        using var fixture = TokenService(store, keys, []);

        Assert.Null(await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        store.Clear();

        Assert.False(fixture.Provider.KeyUnavailable);
    }

    /// <summary>A device still on a credential whose key cannot be opened goes on with its
    /// secret: it syncs, so nothing is unavailable.</summary>
    [Fact]
    public async Task A_device_on_a_credential_whose_key_cannot_be_opened_goes_on_with_the_secret()
    {
        var keys = new UnreliableKeyStore { OpenFails = true };
        var store = new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, Device, "Workshop PC", "a-registration-credential"));
        var sent = new List<DeviceTokenRequest>();
        using var fixture = TokenService(store, keys, sent);

        Assert.Equal("token-0", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));

        var request = Assert.Single(sent);
        Assert.Equal("a-registration-credential", request.Credential);
        Assert.Null(request.PublicKey);
        Assert.False(fixture.Provider.KeyUnavailable);
    }

    /// <summary>The device was unpaired, or paired again as another, while its key was
    /// being opened: the failure judged a device that no longer exists, so the new one
    /// is not told its key is gone.</summary>
    [Fact]
    public async Task A_key_failure_for_a_credential_replaced_mid_fetch_is_not_reported()
    {
        var store = new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, Device, "Workshop PC", null));
        var keys = new UnreliableKeyStore
        {
            OpenFails = true,
            Opening = () => store.SaveAsync(new DeviceCredential(Owner, Guid.NewGuid(), "Laptop", null)).GetAwaiter().GetResult(),
        };
        using var fixture = TokenService(store, keys, []);
        var raised = 0;
        fixture.Provider.KeyUnavailableChanged += () => raised++;

        Assert.Null(await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));

        Assert.False(fixture.Provider.KeyUnavailable);
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task A_credential_forgotten_mid_fetch_is_not_reported_as_a_key_failure()
    {
        var store = new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, Device, "Workshop PC", null));
        var keys = new UnreliableKeyStore { OpenFails = true, Opening = store.Clear };
        using var fixture = TokenService(store, keys, []);

        Assert.Null(await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));

        Assert.False(fixture.Provider.KeyUnavailable);
    }

    /// <summary>A key that keeps failing is a warning when it starts failing and a
    /// debug line on every fetch after, until it works again - not a warning per
    /// background sync.</summary>
    [Fact]
    public async Task A_key_that_keeps_failing_is_warned_about_once_per_change()
    {
        var keys = new UnreliableKeyStore { OpenFails = true };
        var store = new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, Device, "Workshop PC", null));
        var log = new CapturingLogger<SyncTokenProvider>();
        using var fixture = TokenService(store, keys, [], log: log);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Null(await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        }

        var warnings = log.Lines.Count(line => line.Level == LogLevel.Warning);
        Assert.InRange(warnings, 1, 2);
        Assert.Contains(log.Lines, line => line.Level == LogLevel.Debug);

        keys.OpenFails = false;
        Assert.NotNull(await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal(warnings, log.Lines.Count(line => line.Level == LogLevel.Warning));

        keys.OpenFails = true;
        fixture.Provider.Invalidate();
        Assert.Null(await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal(warnings * 2, log.Lines.Count(line => line.Level == LogLevel.Warning));
    }

    [Fact]
    public async Task A_key_that_keeps_failing_on_registration_is_warned_about_once()
    {
        var keys = new UnreliableKeyStore { OpenFails = true };
        var store = new InMemoryDeviceCredentialStore();
        var log = new CapturingLogger<DevicePairingClient>();
        using var http = PairingService([], out var client, store, keys, log);

        Assert.True((await client.RegisterAsync("Workshop PC", TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await client.RegisterAsync("Workshop PC", TestContext.Current.CancellationToken)).IsSuccess);

        Assert.Single(log.Lines, line => line.Level == LogLevel.Warning);
        Assert.Single(log.Lines, line => line.Level == LogLevel.Debug);

        keys.OpenFails = false;
        Assert.True((await client.RegisterAsync("Workshop PC", TestContext.Current.CancellationToken)).IsSuccess);
        keys.OpenFails = true;
        Assert.True((await client.RegisterAsync("Workshop PC", TestContext.Current.CancellationToken)).IsSuccess);

        Assert.Equal(2, log.Lines.Count(line => line.Level == LogLevel.Warning));
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
        List<string> bodies,
        out DevicePairingClient client,
        IDeviceCredentialStore store,
        IDeviceKeyStore? keys,
        ILogger<DevicePairingClient>? log = null)
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
        client = new DevicePairingClient(http, store, browser: new NoBrowser(), keys: keys, log: log);
        return http;
    }

    /// <summary>The token endpoint, recording each request it is sent; <paramref name="refuse"/>
    /// names the status to answer a request with instead of a token, or null to mint one.</summary>
    private static SyncTokenProviderTests.Fixture TokenService(
        IDeviceCredentialStore store,
        IDeviceKeyStore? keys,
        List<DeviceTokenRequest> sent,
        Func<DeviceTokenRequest, HttpStatusCode?>? refuse = null,
        ILogger<SyncTokenProvider>? log = null)
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
            keys: keys,
            log: log);
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

    /// <summary>A key store that fails on demand, the way a TPM in lockout does.</summary>
    private sealed class UnreliableKeyStore : IDeviceKeyStore
    {
        private readonly FailingKey _key = new();

        public bool OpenFails { get; set; }

        /// <summary>Run as the key is opened, before it fails or succeeds - what
        /// happens elsewhere while a TPM takes its time.</summary>
        public Action? Opening { get; init; }

        public bool SignFails
        {
            get => _key.Fails;
            set => _key.Fails = value;
        }

        public DeviceKeyProtection Protection => DeviceKeyProtection.Hardware;

        public IDeviceKey GetOrCreate()
        {
            Opening?.Invoke();
            return OpenFails ? throw new CryptographicException("The TPM is locked out.") : _key;
        }

        private sealed class FailingKey : IDeviceKey
        {
            private readonly ECDsa _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            public bool Fails { get; set; }

            public string PublicKey => Base64Url.EncodeToString(_ecdsa.ExportSubjectPublicKeyInfo());

            public byte[] Sign(ReadOnlySpan<byte> data) => Fails
                ? throw new CryptographicException("The TPM would not sign.")
                : _ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
    }

    /// <summary>A credential store whose writes fail until <see cref="Writable"/> is set.</summary>
    private sealed class UnwritableCredentialStore(DeviceCredential credential) : IDeviceCredentialStore
    {
        private readonly InMemoryDeviceCredentialStore _inner = new(credential);

        public bool Writable { get; set; }

        public event Action? Changed
        {
            add => _inner.Changed += value;
            remove => _inner.Changed -= value;
        }

        public DeviceCredential? Current => _inner.Current;

        public string StorePath => _inner.StorePath;

        public Task SaveAsync(DeviceCredential credential, CancellationToken cancellationToken = default) =>
            Writable ? _inner.SaveAsync(credential, cancellationToken) : throw new IOException("The disk is full.");

        public void Clear() => _inner.Clear();
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
