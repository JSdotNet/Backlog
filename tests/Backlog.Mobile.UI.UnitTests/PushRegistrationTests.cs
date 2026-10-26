using System.Net;
using System.Net.Http.Json;
using System.Text;

using Backlog.Infrastructure.Sync;
using Backlog.Mobile.UI.Notifications;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone hands the sync service its FCM token behind its device token (local ADR 0039 §4),
/// so the service can keep this device's Notification Hubs installation: once per device and
/// token, again when Firebase issues a new token or the phone is paired anew, and never while
/// the phone is not paired or has no token.
/// </summary>
public sealed class PushRegistrationTests
{
    private readonly PushService _service = new();
    private readonly InMemoryDeviceCredentialStore _credentials = new(Credential());
    private readonly ScriptedTokenSource _tokens = new() { Token = "fcm-token-1" };
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 21, 9, 41, 0, TimeSpan.Zero));

    private static DeviceCredential Credential() => new(Guid.NewGuid(), Guid.NewGuid(), "Phone", "a-registration-credential");

    private PushRegistration Registration() => new(
        _tokens,
        _credentials,
        new CloudSyncClient(new HttpClient(_service) { BaseAddress = new Uri("https://sync.test") }),
        _clock);

    [Fact]
    public async Task A_paired_phone_registers_its_token_with_the_service()
    {
        using var registration = Registration();

        Assert.True(await registration.RegisterAsync(TestContext.Current.CancellationToken));

        var put = Assert.Single(_service.Requests);
        Assert.Equal((HttpMethod.Put, "/api/sync/notifications/push", "fcm-token-1"), (put.Method, put.Path, put.Token));
    }

    [Fact]
    public async Task The_same_token_on_the_same_device_registers_once()
    {
        using var registration = Registration();

        await registration.RegisterAsync(TestContext.Current.CancellationToken);
        await registration.RegisterAsync(TestContext.Current.CancellationToken);

        Assert.Single(_service.Requests);
    }

    [Fact]
    public async Task A_new_token_from_firebase_registers_again()
    {
        using var registration = Registration();
        await registration.RegisterAsync(TestContext.Current.CancellationToken);

        await registration.TokenRefreshedAsync("fcm-token-2", TestContext.Current.CancellationToken);

        Assert.Equal(["fcm-token-1", "fcm-token-2"], _service.Requests.Select(request => request.Token));
    }

    [Fact]
    public async Task Pairing_anew_registers_the_token_for_the_new_device()
    {
        using var registration = Registration();
        registration.Start();
        await registration.Pending;

        await _credentials.SaveAsync(Credential(), TestContext.Current.CancellationToken);
        await registration.Pending;

        Assert.Equal(2, _service.Requests.Count);
    }

    [Fact]
    public async Task An_unpaired_phone_registers_nothing()
    {
        _credentials.Clear();
        using var registration = Registration();

        Assert.False(await registration.RegisterAsync(TestContext.Current.CancellationToken));
        Assert.Empty(_service.Requests);
    }

    [Fact]
    public async Task A_host_with_no_push_registers_nothing()
    {
        _tokens.Token = null;
        using var registration = Registration();

        Assert.False(await registration.RegisterAsync(TestContext.Current.CancellationToken));
        Assert.Empty(_service.Requests);
    }

    [Fact]
    public async Task A_service_without_a_hub_is_not_asked_again_for_the_same_token()
    {
        _service.Registered = false;
        using var registration = Registration();

        Assert.False(await registration.RegisterAsync(TestContext.Current.CancellationToken));
        await registration.RegisterAsync(TestContext.Current.CancellationToken);

        Assert.Single(_service.Requests);
    }

    [Fact]
    public async Task A_service_that_could_not_register_is_asked_again_next_time()
    {
        _service.Status = HttpStatusCode.ServiceUnavailable;
        using var registration = Registration();

        Assert.False(await registration.RegisterAsync(TestContext.Current.CancellationToken));

        _service.Status = HttpStatusCode.OK;
        Assert.True(await registration.RegisterAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, _service.Requests.Count);
    }

    [Fact]
    public async Task No_network_is_not_an_error_the_caller_sees()
    {
        _service.Unreachable = true;
        using var registration = Registration();

        Assert.False(await registration.RegisterAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_token_source_that_never_answers_gives_up_after_the_call_budget()
    {
        _tokens.NeverAnswers = true;
        using var registration = Registration();

        var registering = registration.RegisterAsync(TestContext.Current.CancellationToken);
        Assert.False(registering.IsCompleted);

        _clock.Advance(PushRegistration.CallBudget);

        Assert.False(await registering.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Empty(_service.Requests);
    }

    private sealed class ScriptedTokenSource : IPushTokenSource
    {
        public string? Token { get; set; }

        /// <summary>Play Services never completing the token task: only cancellation ends the wait.</summary>
        public bool NeverAnswers { get; set; }

        public Task<string?> GetTokenAsync(CancellationToken cancellationToken) =>
            NeverAnswers
                ? new TaskCompletionSource<string?>().Task.WaitAsync(cancellationToken)
                : Task.FromResult(Token);
    }

    private sealed record PushRequest(HttpMethod Method, string Path, string? Token);

    /// <summary>The service's push route: records each registration and answers as told.</summary>
    private sealed class PushService : HttpMessageHandler
    {
        private readonly Lock _lock = new();
        private readonly List<PushRequest> _requests = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public bool Registered { get; set; } = true;

        public bool Unreachable { get; set; }

        public IReadOnlyList<PushRequest> Requests
        {
            get
            {
                lock (_lock) return [.. _requests];
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Unreachable) throw new HttpRequestException("No such host is known. (sync.test:443)");

            var body = await request.Content!.ReadFromJsonAsync<RegisterPushInstallationRequest>(cancellationToken);
            lock (_lock) _requests.Add(new PushRequest(request.Method, request.RequestUri!.AbsolutePath, body?.FcmToken));

            return Status == HttpStatusCode.OK
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new RegisterPushInstallationResponse(Registered)) }
                : new HttpResponseMessage(Status)
                {
                    Content = new StringContent("""{"title":"Unavailable","status":503,"code":"sync.push_unavailable"}""", Encoding.UTF8, "application/problem+json")
                };
        }
    }
}
