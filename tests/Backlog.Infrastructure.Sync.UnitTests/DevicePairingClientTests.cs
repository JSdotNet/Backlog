using System.Net;
using System.Text.Json;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

using Polly.CircuitBreaker;
using Polly.Timeout;

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

        var resultValue = Assert.IsType<Ok<DeviceCredential>>(result.Value).Value;
        Assert.Equal("/api/sync/devices/register", fixture.Handler.Requests[^1].Path);
        Assert.Equal(new DeviceCredential(Owner, Device, "Workshop PC", "a-registration-credential"), store.Current);
        Assert.Equal(store.Current, resultValue);
    }

    [Fact]
    public async Task Pairing_stores_the_credential_and_sends_the_code_normalized()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (_, _) => Registration());

        // As a person would paste it back: the displayed hyphen, lower case.
        var result = await fixture.Client.PairAsync("k7mn-9pqr", "Phone", TestContext.Current.CancellationToken);

        Assert.IsType<Ok<DeviceCredential>>(result.Value);
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

        var error = Assert.IsType<Error>(result.Value);
        Assert.Equal(SyncErrorCodes.PairingCodeMalformed, error.Code);
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

        var error = Assert.IsType<Error>(result.Value);
        Assert.Equal(SyncErrorCodes.PairingCodeUsed, error.Code);
        Assert.Equal("That code has already paired a device.", error.Message);
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

        var error = Assert.IsType<Error>(result.Value);
        Assert.Equal(SyncErrorCodes.PairingCodeExpired, error.Code);
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

        Assert.Equal("K7MN9PQR", Assert.IsType<Ok<PairingCodeResponse>>(code.Value).Value.Code);
        Assert.Equal("/api/sync/devices/codes", fixture.Handler.Requests[0].Path);

        Assert.Equal(2, Assert.IsType<Ok<DeviceStatusResponse>>(status.Value).Value.PairedDeviceCount);
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

        var error = Assert.IsType<Error>(result.Value);
        Assert.Equal(DevicePairingClient.UnreachableCode, error.Code);
        Assert.Null(store.Current);
    }

    /// <summary>
    /// The resilience pipeline a host puts on the client does not fail with an
    /// <see cref="HttpRequestException"/> when it gives up: a call that hung until
    /// the total timeout ends in Polly's own exception, and a circuit held open
    /// in another. Both are the service not answering, and both are results — the
    /// alternative is the catch-all in every worker, which says nothing.
    /// </summary>
    [Fact]
    public async Task A_service_that_did_not_answer_in_time_reports_itself_rather_than_throwing()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (_, _) => throw new TimeoutRejectedException("The operation didn't complete within the allowed timeout of '00:00:30'."));

        var result = await fixture.Client.RegisterAsync("Workshop PC", TestContext.Current.CancellationToken);

        var error = Assert.IsType<Error>(result.Value);
        Assert.Equal(DevicePairingClient.UnreachableCode, error.Code);
        Assert.Equal("The sync service did not answer in time.", error.Message);
    }

    [Fact]
    public async Task A_circuit_held_open_reports_itself_rather_than_throwing()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (_, _) => throw new BrokenCircuitException("The circuit is now open and is not allowing calls."));

        var result = await fixture.Client.RegisterAsync("Workshop PC", TestContext.Current.CancellationToken);

        var error = Assert.IsType<Error>(result.Value);
        Assert.Equal(DevicePairingClient.UnreachableCode, error.Code);
        Assert.StartsWith("The sync service could not be reached: ", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_device_has_to_be_called_something()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (_, _) => Registration());

        var result = await fixture.Client.RegisterAsync("   ", TestContext.Current.CancellationToken);

        var error = Assert.IsType<Error>(result.Value);
        Assert.Equal(SyncErrorCodes.DeviceNameRequired, error.Code);
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

        var error = Assert.IsType<Error>(result.Value);
        Assert.Equal(DevicePairingClient.CredentialStoreFailedCode, error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.Null(store.Current);
    }

    /// <summary>
    /// The pairing client awaits the store's save rather than firing it: a write
    /// that fails only once it has been handed off — the Keystore refusing after
    /// the call returned — is still a failed pairing, never a success reported
    /// ahead of a write that then did not land.
    /// </summary>
    [Fact]
    public async Task A_credential_whose_save_fails_after_it_was_handed_off_is_a_failed_pairing()
    {
        var store = new UnwritableDeviceCredentialStore { FailsLater = true };
        using var fixture = Fixture.Create(store, (_, _) => Registration());

        var result = await fixture.Client.PairAsync("K7MN-9PQR", "Phone", TestContext.Current.CancellationToken);

        var error = Assert.IsType<Error>(result.Value);
        Assert.Equal(DevicePairingClient.CredentialStoreFailedCode, error.Code);
        Assert.Equal(1, store.Saves);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task Registering_redeems_the_sign_in_code_with_the_verifier_behind_its_challenge()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (_, _) => Registration());

        await fixture.Client.RegisterAsync("Workshop PC", TestContext.Current.CancellationToken);

        var register = JsonSerializer.Deserialize<RegisterDeviceRequest>(fixture.LastBody, JsonSerializerOptions.Web)!;
        Assert.Equal(EnrolmentCode, register.EnrolmentCode);
        Assert.Equal(EnrolmentPkce.Method, fixture.Start!.CodeChallengeMethod);
        Assert.Equal(fixture.Start.CodeChallenge, EnrolmentPkce.Challenge(register.CodeVerifier!));
        Assert.True(EnrolmentPkce.IsLoopbackRedirect(fixture.Start.RedirectUri));
    }

    /// <summary>The GitHub shape: the browser opens the sign-in page, and the answer
    /// arrives over HTTP at this machine's loopback.</summary>
    [Fact]
    public async Task The_owner_signs_in_through_the_browser_and_the_answer_comes_back_to_the_loopback()
    {
        var store = new InMemoryDeviceCredentialStore();
        var gitHub = new Uri("https://github.test/login/oauth/authorize?state=service-state");
        Fixture? fixture = null;
        var browser = new LoopbackBrowser(() => fixture!.Start!, start => $"code={EnrolmentCode}&state={Uri.EscapeDataString(start.State)}");
        fixture = Fixture.Create(store, (_, _) => Registration(), browser, gitHub);
        using var _ = fixture;

        var result = await fixture.Client.RegisterAsync("Workshop PC", TestContext.Current.CancellationToken);

        Assert.True(result is Ok<DeviceCredential>, result is Error error ? error.Message : null);
        Assert.Equal(gitHub, browser.Opened);
        Assert.Equal(EnrolmentCode, JsonSerializer.Deserialize<RegisterDeviceRequest>(fixture.LastBody, JsonSerializerOptions.Web)!.EnrolmentCode);
    }

    [Fact]
    public async Task A_refused_sign_in_says_why_and_registers_nothing()
    {
        var store = new InMemoryDeviceCredentialStore();
        Fixture? fixture = null;
        var browser = new LoopbackBrowser(
            () => fixture!.Start!,
            start => $"error=access_denied&error_description={Uri.EscapeDataString("Only the owner's GitHub account can enrol a device.")}&state={Uri.EscapeDataString(start.State)}");
        fixture = Fixture.Create(store, (_, _) => Registration(), browser, new Uri("https://github.test/authorize"));
        using var _ = fixture;

        var result = await fixture.Client.RegisterAsync("Workshop PC", TestContext.Current.CancellationToken);

        var resultError = Assert.IsType<Error>(result.Value);
        Assert.Equal(DevicePairingClient.SignInRefusedCode, resultError.Code);
        Assert.Equal("Only the owner's GitHub account can enrol a device.", resultError.Message);
        Assert.DoesNotContain(fixture.Handler.Requests, r => r.Path == SyncRoutes.Absolute(SyncRoutes.RegisterDevice));
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task An_answer_for_another_sign_in_is_refused()
    {
        var store = new InMemoryDeviceCredentialStore();
        Fixture? fixture = null;
        var browser = new LoopbackBrowser(() => fixture!.Start!, _ => $"code={EnrolmentCode}&state=someone-elses");
        fixture = Fixture.Create(store, (_, _) => Registration(), browser, new Uri("https://github.test/authorize"));
        using var _ = fixture;

        var result = await fixture.Client.RegisterAsync("Workshop PC", TestContext.Current.CancellationToken);

        var error = Assert.IsType<Error>(result.Value);
        Assert.Equal(DevicePairingClient.SignInRefusedCode, error.Code);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task A_sign_in_nobody_finishes_times_out()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(
            store, (_, _) => Registration(), new IgnoringBrowser(), new Uri("https://github.test/authorize"), TimeSpan.FromMilliseconds(200));

        var result = await fixture.Client.RegisterAsync("Workshop PC", TestContext.Current.CancellationToken);

        var error = Assert.IsType<Error>(result.Value);
        Assert.Equal(DevicePairingClient.SignInTimedOutCode, error.Code);
    }

    /// <summary>A socket that opens and says nothing — a browser's preconnect — must not
    /// hold the real redirect behind it until the sign-in times out.</summary>
    [Fact]
    public async Task An_idle_connection_to_the_loopback_does_not_hold_up_the_answer()
    {
        var store = new InMemoryDeviceCredentialStore();
        Fixture? fixture = null;
        using var idle = new System.Net.Sockets.TcpClient();
        var browser = new LoopbackBrowser(
            () =>
            {
                var start = fixture!.Start!;
                idle.Connect(System.Net.IPAddress.Loopback, start.RedirectUri.Port);
                return start;
            },
            start => $"code={EnrolmentCode}&state={Uri.EscapeDataString(start.State)}");
        fixture = Fixture.Create(store, (_, _) => Registration(), browser, new Uri("https://github.test/authorize"), TimeSpan.FromSeconds(30));
        using var _ = fixture;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var result = await fixture.Client.RegisterAsync("Workshop PC", TestContext.Current.CancellationToken);

        Assert.True(result is Ok<DeviceCredential>, result is Error error ? error.Message : null);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"The answer took {clock.Elapsed.TotalSeconds:0.0} s.");
    }

    [Fact]
    public async Task A_host_with_no_browser_is_told_to_pair_by_code()
    {
        var store = new InMemoryDeviceCredentialStore();
        var handler = new StubHttpMessageHandler((_, _) => Registration());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };

        var result = await new DevicePairingClient(http, store).RegisterAsync("Phone", TestContext.Current.CancellationToken);

        var error = Assert.IsType<Error>(result.Value);
        Assert.Equal(DevicePairingClient.SignInUnavailableCode, error.Code);
        Assert.Empty(handler.Requests);
    }

    private const string EnrolmentCode = "one-time-enrolment-code";

    /// <summary>A browser the Development shape never opens.</summary>
    private sealed class UnusedBrowser : ISystemBrowser
    {
        public Task OpenAsync(Uri address, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"No browser should open, but {address} was.");
    }

    /// <summary>A browser the person never finishes in.</summary>
    private sealed class IgnoringBrowser : ISystemBrowser
    {
        public Task OpenAsync(Uri address, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Plays GitHub and the browser: sends a real GET to the device's loopback
    /// with the answer <paramref name="query"/> builds.</summary>
    private sealed class LoopbackBrowser(Func<StartEnrolmentRequest> start, Func<StartEnrolmentRequest, string> query) : ISystemBrowser
    {
        public Uri? Opened { get; private set; }

        public Task OpenAsync(Uri address, CancellationToken cancellationToken = default)
        {
            Opened = address;
            var request = start();
            var answer = new Uri($"{request.RedirectUri.AbsoluteUri}?{query(request)}");

            var cancellation = TestContext.Current.CancellationToken;
            _ = Task.Run(async () =>
            {
                using var browser = new HttpClient();
                using var _ = await browser.GetAsync(answer, cancellation);
            }, cancellation);

            return Task.CompletedTask;
        }
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

        /// <summary>Fails through the returned task, after yielding, rather than
        /// by throwing from the call.</summary>
        public bool FailsLater { get; init; }

        public int Saves { get; private set; }

        public async Task SaveAsync(DeviceCredential credential, CancellationToken cancellationToken = default)
        {
            Saves++;

            if (!FailsLater) throw new IOException("There is not enough space on the disk.");

            await Task.Yield();
            throw new IOException("The secure store refused the write.");
        }

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

        /// <summary>The enrolment start's body, once the client has sent it.</summary>
        public StartEnrolmentRequest? Start { get; private set; }

        /// <summary>
        /// A client over a stubbed service. The enrolment start is answered here, the way
        /// a Development service with no owner answers it — straight back to the loopback
        /// with <see cref="EnrolmentCode"/> — unless <paramref name="signInAddress"/> names
        /// a sign-in page for <paramref name="browser"/> to open.
        /// </summary>
        public static Fixture Create(
            IDeviceCredentialStore credentials,
            Func<HttpRequestMessage, int, HttpResponseMessage> respond,
            ISystemBrowser? browser = null,
            Uri? signInAddress = null,
            TimeSpan? signInTimeout = null,
            IDeviceKeyStore? keys = null)
        {
            var bodies = new List<string>();
            Fixture? fixture = null;

            var handler = new StubHttpMessageHandler((request, index) =>
            {
                string? body = null;
                if (request.Content is not null)
                {
                    body = request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
                    bodies.Add(body);
                }

                if (request.RequestUri!.AbsolutePath == SyncRoutes.Absolute(SyncRoutes.EnrolmentStart))
                {
                    var start = JsonSerializer.Deserialize<StartEnrolmentRequest>(body!, JsonSerializerOptions.Web)!;
                    fixture!.Start = start;
                    var address = signInAddress
                        ?? new Uri($"{start.RedirectUri.AbsoluteUri}?code={EnrolmentCode}&state={Uri.EscapeDataString(start.State)}");

                    return StubHttpMessageHandler.Json(
                        HttpStatusCode.OK, JsonSerializer.Serialize(new EnrolmentStartResponse(address), JsonSerializerOptions.Web));
                }

                return respond(request, index);
            });

            var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };

            fixture = new Fixture(
                http,
                handler,
                new DevicePairingClient(http, credentials, browser: browser ?? new UnusedBrowser(), signInTimeout: signInTimeout, keys: keys),
                bodies);
            return fixture;
        }

        public void Dispose() => _http.Dispose();
    }
}
