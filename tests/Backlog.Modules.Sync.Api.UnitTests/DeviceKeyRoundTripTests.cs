using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Backlog.Infrastructure.Sync;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;
using Backlog.Tests;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// The device key end to end (local ADR 0058 §2): the real
/// <see cref="DevicePairingClient"/> and <see cref="SyncTokenProvider"/> against the
/// real sync service, so the client's requests and the service's rules are proved
/// against each other rather than each against a stub of the other. Registration is
/// the Development enrolment the other tests use: the service answers the loopback
/// with the code on it, and no browser opens.
/// </summary>
public sealed class DeviceKeyRoundTripTests : IDisposable
{
    private readonly SyncServiceFactory _service = new();
    private readonly List<IDisposable> _owned = [];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var owned in Enumerable.Reverse(_owned)) owned.Dispose();
        _service.Dispose();
    }

    [Fact]
    public async Task A_device_registered_with_its_key_signs_for_a_token_that_syncs_and_keeps_no_secret()
    {
        var device = Device(new InMemoryDeviceCredentialStore(), new InMemoryDeviceKeyStore());

        var registered = Assert.IsType<Ok<DeviceCredential>>((await device.Pairing.RegisterAsync("Workshop PC", Cancellation)).Value).Value;

        Assert.Null(device.Credentials.Current!.Credential);

        var token = await device.Tokens.GetAccessTokenAsync(Cancellation);

        Assert.NotNull(token);
        Assert.Equal(registered.DeviceId, await StatusAsync(token));
        Assert.Null(device.Credentials.Current!.Credential);
    }

    [Fact]
    public async Task A_device_paired_with_its_key_signs_for_its_token()
    {
        var first = Device(new InMemoryDeviceCredentialStore(), new InMemoryDeviceKeyStore());
        Assert.IsType<Ok<DeviceCredential>>((await first.Pairing.RegisterAsync("Workshop PC", Cancellation)).Value);

        var code = Assert.IsType<Ok<PairingCodeResponse>>((await first.Pairing.IssuePairingCodeAsync(Cancellation)).Value).Value;

        var second = Device(new InMemoryDeviceCredentialStore(), new InMemoryDeviceKeyStore());
        var paired = Assert.IsType<Ok<DeviceCredential>>((await second.Pairing.PairAsync(code.Code, "Laptop", Cancellation)).Value).Value;

        Assert.Null(second.Credentials.Current!.Credential);

        var token = await second.Tokens.GetAccessTokenAsync(Cancellation);
        Assert.Equal(paired.DeviceId, await StatusAsync(token));
    }

    /// <summary>A device registered on a credential, before keys, moves onto its key on
    /// the next fetch from a host that keeps one: the secret is forgotten here, the next
    /// fetch is an assertion, and the service refuses the old secret from then on.</summary>
    [Fact]
    public async Task A_credential_device_moves_onto_its_key_and_its_old_secret_is_refused()
    {
        var credentials = new InMemoryDeviceCredentialStore();
        var before = Device(credentials, keys: null);
        var registered = Assert.IsType<Ok<DeviceCredential>>((await before.Pairing.RegisterAsync("Workshop PC", Cancellation)).Value).Value;
        var secret = credentials.Current!.Credential;
        Assert.NotNull(secret);

        var after = Device(credentials, new InMemoryDeviceKeyStore());

        var migrated = await after.Tokens.GetAccessTokenAsync(Cancellation);
        Assert.Equal(registered.DeviceId, await StatusAsync(migrated));
        Assert.Null(credentials.Current!.Credential);

        var sent = new List<string>();
        var signer = Device(credentials, after.Keys, sent);
        var signed = await signer.Tokens.GetAccessTokenAsync(Cancellation);
        Assert.Equal(registered.DeviceId, await StatusAsync(signed));
        var request = JsonSerializer.Deserialize<DeviceTokenRequest>(Assert.Single(sent), JsonSerializerOptions.Web)!;
        Assert.Null(request.Credential);
        Assert.NotNull(request.ClientAssertion);

        using var client = _service.CreateClient();
        var old = await client.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.DeviceToken), new DeviceTokenRequest(registered.DeviceId, secret), Cancellation);
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
    }

    /// <summary>The service bound the key, but this machine could not write the record
    /// without its secret. The token is still used; the next fetch re-sends the
    /// migration, the service answers it on its assertion, and the secret goes then.</summary>
    [Fact]
    public async Task A_migration_resent_after_the_bind_succeeds_and_forgets_the_secret()
    {
        var credentials = new SaveFailingCredentialStore();
        var before = Device(credentials, keys: null);
        var registered = Assert.IsType<Ok<DeviceCredential>>((await before.Pairing.RegisterAsync("Workshop PC", Cancellation)).Value).Value;

        var after = Device(credentials, new InMemoryDeviceKeyStore());
        credentials.FailSaves = true;

        var first = await after.Tokens.GetAccessTokenAsync(Cancellation);
        Assert.Equal(registered.DeviceId, await StatusAsync(first));
        Assert.NotNull(credentials.Current!.Credential);

        credentials.FailSaves = false;
        after.Tokens.Invalidate();

        var second = await after.Tokens.GetAccessTokenAsync(Cancellation);
        Assert.Equal(registered.DeviceId, await StatusAsync(second));
        Assert.Null(credentials.Current!.Credential);
        Assert.False(after.Tokens.CredentialRejected);
    }

    /// <summary>An assertion the client sent, captured and sent again as it was: the
    /// service took it once and refuses it from then on.</summary>
    [Fact]
    public async Task A_replayed_assertion_is_refused()
    {
        var registrar = Device(new InMemoryDeviceCredentialStore(), new InMemoryDeviceKeyStore());
        Assert.IsType<Ok<DeviceCredential>>((await registrar.Pairing.RegisterAsync("Workshop PC", Cancellation)).Value);

        var sent = new List<string>();
        var device = Device(registrar.Credentials, registrar.Keys, sent);
        Assert.NotNull(await device.Tokens.GetAccessTokenAsync(Cancellation));

        using var client = _service.CreateClient();
        using var replayed = await client.PostAsync(
            SyncRoutes.Absolute(SyncRoutes.DeviceToken),
            new StringContent(Assert.Single(sent), Encoding.UTF8, "application/json"),
            Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);
    }

    /// <summary>The same device record on another machine, with a key of its own: its
    /// assertions are signed by a key the service never bound.</summary>
    [Fact]
    public async Task An_assertion_signed_by_another_key_is_refused()
    {
        var registered = Device(new InMemoryDeviceCredentialStore(), new InMemoryDeviceKeyStore());
        Assert.IsType<Ok<DeviceCredential>>((await registered.Pairing.RegisterAsync("Workshop PC", Cancellation)).Value);

        var impostor = Device(registered.Credentials, new InMemoryDeviceKeyStore());

        Assert.Null(await impostor.Tokens.GetAccessTokenAsync(Cancellation));
        Assert.True(impostor.Tokens.CredentialRejected);
    }

    /// <summary>A device that has moved onto its key, still holding its secret here - the
    /// save that would have forgotten it failed - on a host with no key store: the
    /// secret alone is no longer a way in.</summary>
    [Fact]
    public async Task A_bound_device_sending_its_secret_alone_is_refused()
    {
        var credentials = new SaveFailingCredentialStore();
        var before = Device(credentials, keys: null);
        Assert.IsType<Ok<DeviceCredential>>((await before.Pairing.RegisterAsync("Workshop PC", Cancellation)).Value);

        credentials.FailSaves = true;
        var migrated = Device(credentials, new InMemoryDeviceKeyStore());
        Assert.NotNull(await migrated.Tokens.GetAccessTokenAsync(Cancellation));
        Assert.NotNull(credentials.Current!.Credential);

        var keyless = Device(credentials, keys: null);

        Assert.Null(await keyless.Tokens.GetAccessTokenAsync(Cancellation));
        Assert.True(keyless.Tokens.CredentialRejected);
    }

    /// <summary>A migration whose assertion was signed by a key other than the one it
    /// offers proves nothing about that key: refused, nothing bound, and the device goes
    /// on with its secret.</summary>
    [Fact]
    public async Task A_migration_signed_by_a_key_other_than_the_one_offered_is_refused()
    {
        var credentials = new InMemoryDeviceCredentialStore();
        var before = Device(credentials, keys: null);
        var registered = Assert.IsType<Ok<DeviceCredential>>((await before.Pairing.RegisterAsync("Workshop PC", Cancellation)).Value).Value;
        var secret = credentials.Current!.Credential;

        using var offered = new DeviceSigningKey();
        using var signer = new DeviceSigningKey();
        var deviceId = registered.DeviceId;

        using var client = _service.CreateClient();
        using var refused = await client.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.DeviceToken),
            new DeviceTokenRequest(
                deviceId,
                secret,
                SyncAssertion.ClientAssertionType,
                signer.Assertion(deviceId, DateTimeOffset.UtcNow),
                offered.PublicKey),
            Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(deviceId, await StatusAsync(await before.Tokens.GetAccessTokenAsync(Cancellation)));
    }

    /// <summary>The device a test drives: the pairing client and the token provider
    /// as a head composes them, over the service's in-process handler.</summary>
    /// <param name="tokenRequests">Collects the body of every request the token
    /// provider sends, for a test that sends one again itself.</param>
    private Client Device(IDeviceCredentialStore credentials, IDeviceKeyStore? keys, List<string>? tokenRequests = null)
    {
        if (keys is IDisposable disposableKeys && !_owned.Contains(disposableKeys)) _owned.Add(disposableKeys);

        var baseAddress = _service.Server.BaseAddress;
        var services = new ServiceCollection();
        services.AddHttpClient(SyncTokenProvider.HttpClientName, client => client.BaseAddress = baseAddress)
            .ConfigurePrimaryHttpMessageHandler(() => new TokenRequestRecorder(tokenRequests) { InnerHandler = _service.Server.CreateHandler() });
        var provider = services.BuildServiceProvider();
        _owned.Add(provider);

        var tokens = new SyncTokenProvider(
            provider.GetRequiredService<IHttpClientFactory>(), credentials, TimeProvider.System, keys);
        _owned.Add(tokens);

        var http = new HttpClient(new SyncAuthenticationHandler(tokens) { InnerHandler = _service.Server.CreateHandler() })
        {
            BaseAddress = baseAddress,
        };
        _owned.Add(http);

        return new Client(new DevicePairingClient(http, credentials, browser: new NoBrowser(), keys: keys), tokens, credentials, keys);
    }

    /// <summary>The device id a token's bearer reads back from a bearer endpoint.</summary>
    private async Task<Guid> StatusAsync(string? token)
    {
        Assert.NotNull(token);

        using var client = _service.CreateClient().Bearing(token);
        var status = await client.GetFromJsonAsync<DeviceStatusResponse>(SyncRoutes.Absolute(SyncRoutes.DeviceStatus), Cancellation);
        return status!.DeviceId;
    }

    private sealed record Client(
        DevicePairingClient Pairing, SyncTokenProvider Tokens, IDeviceCredentialStore Credentials, IDeviceKeyStore? Keys);

    /// <summary>Passes every request on, keeping its body when there is somewhere to.</summary>
    private sealed class TokenRequestRecorder(List<string>? bodies) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (bodies is not null && request.Content is not null)
            {
                bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class NoBrowser : ISystemBrowser
    {
        public Task OpenAsync(Uri address, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The Development service answers straight to the loopback.");
    }

    /// <summary>A credential store whose writes fail while <see cref="FailSaves"/> is set,
    /// as a full disk would.</summary>
    private sealed class SaveFailingCredentialStore : IDeviceCredentialStore
    {
        private readonly InMemoryDeviceCredentialStore _inner = new();

        public bool FailSaves { get; set; }

        public event Action? Changed
        {
            add => _inner.Changed += value;
            remove => _inner.Changed -= value;
        }

        public DeviceCredential? Current => _inner.Current;

        public string StorePath => _inner.StorePath;

        public Task SaveAsync(DeviceCredential credential, CancellationToken cancellationToken = default) =>
            FailSaves ? throw new IOException("The disk is full.") : _inner.SaveAsync(credential, cancellationToken);

        public void Clear() => _inner.Clear();
    }
}
