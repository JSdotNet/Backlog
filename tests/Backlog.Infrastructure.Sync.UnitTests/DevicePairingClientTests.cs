using System.Net;

using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The four calls a device makes about its own identity, and what it does with
/// the answers. The two that end in a credential are the interesting ones: the
/// secret is shown once, so a client that returned it to the caller and left the
/// storing to the screen would have as many places to drop it as it has screens.
/// </summary>
public sealed class DevicePairingClientTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Device = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Registering_stores_the_credential_it_was_handed()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (_, _) => Registration());

        var result = await fixture.Client.RegisterAsync("Workshop PC", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("/api/sync/devices/register", fixture.Handler.Requests[0].Path);
        Assert.Equal(new DeviceCredential(Owner, Device, "Workshop PC", "a-registration-credential"), store.Current);
        Assert.Equal(store.Current, result.Value);
    }

    [Fact]
    public async Task Pairing_stores_the_credential_and_sends_the_code_normalized()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (_, _) => Registration());

        // As a person would paste it back: the displayed hyphen, lower case.
        var result = await fixture.Client.PairAsync("k7mn-9pqr", "Phone", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("/api/sync/devices/pair", fixture.Handler.Requests[0].Path);
        Assert.Contains("\"K7MN9PQR\"", fixture.LastBody, StringComparison.Ordinal);
        Assert.Equal("Phone", store.Current?.DeviceName);
    }

    /// <summary>
    /// A code that is not a code never leaves the device. The service would
    /// answer the same way, but a typo costing a round trip is a round trip
    /// spent proving what the shared format already knows.
    /// </summary>
    [Fact]
    public async Task A_malformed_code_is_refused_without_a_request()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (_, _) => Registration());

        var result = await fixture.Client.PairAsync("nope", "Phone", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SyncErrorCodes.PairingCodeMalformed, result.Error.Code);
        Assert.Empty(fixture.Handler.Requests);
        Assert.Null(store.Current);
    }

    /// <summary>
    /// An expected 4xx is data, not an exception: the screen has to say which of
    /// the four things that can be wrong with eight typed characters happened,
    /// and it branches on the code rather than on an English sentence.
    /// </summary>
    [Fact]
    public async Task A_used_code_comes_back_as_its_problem_code_and_detail()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (_, _) => StubHttpMessageHandler.Problem(
            HttpStatusCode.Conflict,
            SyncErrorCodes.PairingCodeUsed,
            "That code has already paired a device."));

        var result = await fixture.Client.PairAsync("K7MN9PQR", "Phone", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SyncErrorCodes.PairingCodeUsed, result.Error.Code);
        Assert.Equal("That code has already paired a device.", result.Error.Message);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task An_expired_code_is_told_apart_from_a_used_one()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (_, _) => StubHttpMessageHandler.Problem(
            HttpStatusCode.NotFound,
            SyncErrorCodes.PairingCodeExpired,
            "That code has expired."));

        var result = await fixture.Client.PairAsync("K7MN9PQR", "Phone", TestContext.Current.CancellationToken);

        Assert.Equal(SyncErrorCodes.PairingCodeExpired, result.Error.Code);
    }

    [Fact]
    public async Task Issuing_a_code_and_reading_the_status_go_to_their_own_routes()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (request, _) =>
            request.RequestUri!.AbsolutePath.EndsWith("codes", StringComparison.Ordinal)
                ? StubHttpMessageHandler.Json(HttpStatusCode.Created, """{"code":"K7MN9PQR","expiresAt":"2026-09-07T09:10:00+00:00"}""")
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"ownerId":"{{Owner}}","deviceId":"{{Device}}","deviceName":"Workshop PC","pairedDeviceCount":2}"""));

        var code = await fixture.Client.IssuePairingCodeAsync(TestContext.Current.CancellationToken);
        var status = await fixture.Client.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal("K7MN9PQR", code.Value.Code);
        Assert.Equal("/api/sync/devices/codes", fixture.Handler.Requests[0].Path);

        Assert.Equal(2, status.Value.PairedDeviceCount);
        Assert.Equal("/api/sync/devices/me", fixture.Handler.Requests[1].Path);
    }

    /// <summary>
    /// A service that is not there is still not an exception. It is filed under
    /// a code of the client's own rather than one of <see cref="SyncErrorCodes"/>,
    /// because nobody issued it.
    /// </summary>
    [Fact]
    public async Task A_service_that_cannot_be_reached_reports_itself_rather_than_throwing()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (_, _) => throw new HttpRequestException("No route to host."));

        var result = await fixture.Client.RegisterAsync("Workshop PC", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(DevicePairingClient.UnreachableCode, result.Error.Code);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task A_device_has_to_be_called_something()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (_, _) => Registration());

        var result = await fixture.Client.RegisterAsync("   ", TestContext.Current.CancellationToken);

        Assert.Equal(SyncErrorCodes.DeviceNameRequired, result.Error.Code);
        Assert.Empty(fixture.Handler.Requests);
    }

    /// <summary>
    /// The service issued a credential and this machine could not keep it. That
    /// is a failed pairing, not a successful one: the screen has to say so,
    /// because the alternative is a device that looks paired until it restarts.
    /// </summary>
    [Fact]
    public async Task A_credential_that_cannot_be_stored_is_a_failed_pairing()
    {
        var store = new UnwritableDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (_, _) => Registration());

        var result = await fixture.Client.RegisterAsync("Workshop PC", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(DevicePairingClient.CredentialStoreFailedCode, result.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
        Assert.Null(store.Current);
    }

    private static HttpResponseMessage Registration() => StubHttpMessageHandler.Json(
        HttpStatusCode.Created,
        $$"""{"ownerId":"{{Owner}}","deviceId":"{{Device}}","credential":"a-registration-credential"}""");

    /// <summary>A store whose disk is full, or whose file is locked, or whose
    /// folder somebody removed. Which one does not matter here — the pairing
    /// client's job is the same for all of them.</summary>
    private sealed class UnwritableDeviceCredentialStore : IDeviceCredentialStore
    {
        public event Action? Changed;

        public DeviceCredential? Current => null;

        public string StorePath => "nowhere";

        public void Save(DeviceCredential credential) => throw new IOException("There is not enough space on the disk.");

        public void Clear() => Changed?.Invoke();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;
        private readonly List<string> _bodies;

        private Fixture(HttpClient http, StubHttpMessageHandler handler, DevicePairingClient client, List<string> bodies)
        {
            _http = http;
            _bodies = bodies;
            Handler = handler;
            Client = client;
        }

        public StubHttpMessageHandler Handler { get; }

        public DevicePairingClient Client { get; }

        /// <summary>What the last request carried, so a test can assert the code
        /// was normalized before it was sent.</summary>
        public string LastBody => _bodies.Count == 0 ? string.Empty : _bodies[^1];

        public static Fixture Create(
            IDeviceCredentialStore credentials,
            Func<HttpRequestMessage, int, HttpResponseMessage> respond)
        {
            var bodies = new List<string>();

            var handler = new StubHttpMessageHandler((request, index) =>
            {
                if (request.Content is not null)
                {
                    bodies.Add(request.Content.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult());
                }

                return respond(request, index);
            });

            var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };

            return new Fixture(http, handler, new DevicePairingClient(http, credentials), bodies);
        }

        public void Dispose() => _http.Dispose();
    }
}
