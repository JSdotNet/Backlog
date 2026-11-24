using System.Net;

using Backlog.Infrastructure.Sync.Extensions;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// Windows Hello before a pairing code is minted, a device revoked, or a pairing refused (local
/// ADR 0058 §6): with a <see cref="DeviceStepUp"/> composed, each call reaches the service only
/// once the person confirms; cancelled, unavailable or — for a refusal — unconfirmed in time,
/// nothing is sent and the failure says which. Without one — the web harness and the phone — the
/// client asks nothing.
/// </summary>
public sealed class DeviceStepUpTests
{
    private static readonly Guid Phone = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task A_confirmed_pairing_code_is_minted()
    {
        var consent = new ScriptedConsent(UserConsentOutcome.Verified);
        var (client, handler) = Client(consent);

        var code = await client.IssuePairingCodeAsync(TestContext.Current.CancellationToken);

        Assert.True(code.IsSuccess);
        Assert.Equal("K7MN9PQR", code.Value.Code);
        Assert.Equal("/api/sync/devices/codes", Assert.Single(handler.Requests).Path);
        Assert.Contains("pairing code", Assert.Single(consent.Prompts), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(UserConsentOutcome.Declined, DeviceStepUp.DeclinedCode, "cancelled")]
    [InlineData(UserConsentOutcome.NotAvailable, DeviceStepUp.UnavailableCode, "not available")]
    public async Task A_pairing_code_the_person_did_not_confirm_is_never_asked_for(
        UserConsentOutcome outcome, string code, string reason)
    {
        var (client, handler) = Client(new ScriptedConsent(outcome));

        var issued = await client.IssuePairingCodeAsync(TestContext.Current.CancellationToken);

        Assert.True(issued.IsFailure);
        Assert.Equal(code, issued.Error.Code);
        Assert.StartsWith("No pairing code was made", issued.Error.Message, StringComparison.Ordinal);
        Assert.Contains(reason, issued.Error.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_confirmed_revocation_reaches_the_service()
    {
        var consent = new ScriptedConsent(UserConsentOutcome.Verified);
        var (client, handler) = Client(consent);

        var revoked = await client.RevokeDeviceAsync(Phone, TestContext.Current.CancellationToken);

        Assert.True(revoked.IsSuccess);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.EndsWith(Phone.ToString("D"), request.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Revoke a device", Assert.Single(consent.Prompts), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(UserConsentOutcome.Declined, DeviceStepUp.DeclinedCode, "cancelled")]
    [InlineData(UserConsentOutcome.NotAvailable, DeviceStepUp.UnavailableCode, "not available")]
    public async Task A_revocation_the_person_did_not_confirm_is_never_sent(
        UserConsentOutcome outcome, string code, string reason)
    {
        var (client, handler) = Client(new ScriptedConsent(outcome));

        var revoked = await client.RevokeDeviceAsync(Phone, TestContext.Current.CancellationToken);

        Assert.True(revoked.IsFailure);
        Assert.Equal(code, revoked.Error.Code);
        Assert.StartsWith("The device was not revoked", revoked.Error.Message, StringComparison.Ordinal);
        Assert.Contains(reason, revoked.Error.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_confirmed_refusal_revokes_the_device()
    {
        var consent = new ScriptedConsent(UserConsentOutcome.Verified);
        var (client, handler) = Client(consent);

        var refused = await client.RefuseDeviceAsync(Phone, TestContext.Current.CancellationToken);

        Assert.True(refused.IsSuccess);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.EndsWith(Phone.ToString("D"), request.Path, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Refuse a device", Assert.Single(consent.Prompts), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(UserConsentOutcome.Declined, DeviceStepUp.DeclinedCode, "Windows Hello was cancelled")]
    [InlineData(UserConsentOutcome.NotAvailable, DeviceStepUp.UnavailableCode, "Windows Hello is not available")]
    public async Task A_refusal_the_person_did_not_confirm_is_never_sent(
        UserConsentOutcome outcome, string code, string reason)
    {
        var (client, handler) = Client(new ScriptedConsent(outcome));

        var refused = await client.RefuseDeviceAsync(Phone, TestContext.Current.CancellationToken);

        Assert.True(refused.IsFailure);
        Assert.Equal(code, refused.Error.Code);
        Assert.StartsWith("The device was not refused", refused.Error.Message, StringComparison.Ordinal);
        Assert.Contains(reason, refused.Error.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    /// <summary>A Refuse answered on the phone raises the prompt on a desktop nobody may be
    /// at, and every answer behind it waits on it: unconfirmed for
    /// <see cref="DevicePairingClient.RefuseConfirmationWindow"/>, the prompt expires, and
    /// nothing is sent.</summary>
    [Fact]
    public async Task A_refusal_nobody_confirms_in_time_expires_and_is_never_sent()
    {
        var clock = new FakeTimeProvider();
        var consent = new UnansweredConsent();
        var (client, handler) = Client(consent, clock);

        var refusing = client.RefuseDeviceAsync(Phone, TestContext.Current.CancellationToken);
        await consent.Shown.Task.WaitAsync(TestContext.Current.CancellationToken);

        clock.Advance(DevicePairingClient.RefuseConfirmationWindow - TimeSpan.FromSeconds(1));
        Assert.False(refusing.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1));
        var refused = await refusing.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(refused.IsFailure);
        Assert.Equal(DeviceStepUp.ExpiredCode, refused.Error.Code);
        Assert.Equal("The device was not refused: Windows Hello was not confirmed within 2 minutes.", refused.Error.Message);
        Assert.Empty(handler.Requests);
    }

    /// <summary>The window bounds the refusal alone: making a code and revoking from the
    /// Devices page are asked of the person at the desktop, who is there to answer.</summary>
    [Fact]
    public async Task Only_the_refusal_prompt_expires()
    {
        var clock = new FakeTimeProvider();
        var consent = new UnansweredConsent();
        var (client, _) = Client(consent, clock);

        var revoking = client.RevokeDeviceAsync(Phone, TestContext.Current.CancellationToken);
        await consent.Shown.Task.WaitAsync(TestContext.Current.CancellationToken);
        clock.Advance(DevicePairingClient.RefuseConfirmationWindow * 2);

        Assert.False(revoking.IsCompleted);
        consent.Answer(UserConsentOutcome.Verified);
        Assert.True((await revoking.WaitAsync(TestContext.Current.CancellationToken)).IsSuccess);
    }

    /// <summary>The app shutting down withdraws the prompt, and Windows Hello answers a
    /// withdrawn prompt as cancelled. That is not the person declining: the act throws, so a
    /// refusal stays undelivered and is replayed on the next start rather than filed as
    /// declined, and nothing is sent.</summary>
    [Theory]
    [InlineData("revoke")]
    [InlineData("refuse")]
    [InlineData("code")]
    public async Task Shutdown_while_Windows_Hello_asks_throws_rather_than_declining(string act)
    {
        var consent = new UnansweredConsent();
        var (client, handler) = Client(consent, new FakeTimeProvider());
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task asking = act switch
        {
            "revoke" => client.RevokeDeviceAsync(Phone, shutdown.Token),
            "refuse" => client.RefuseDeviceAsync(Phone, shutdown.Token),
            _ => client.IssuePairingCodeAsync(shutdown.Token),
        };
        await consent.Shown.Task.WaitAsync(TestContext.Current.CancellationToken);

        await shutdown.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_host_with_no_step_up_refuses_without_asking()
    {
        var (client, handler) = Client(consent: null);

        var refused = await client.RefuseDeviceAsync(Phone, TestContext.Current.CancellationToken);

        Assert.True(refused.IsSuccess);
        Assert.Equal(HttpMethod.Delete, Assert.Single(handler.Requests).Method);
    }

    /// <summary>The typed client the container builds takes the step-up a host registered, as
    /// the desktop head registers it: what the hand-built clients above assume.</summary>
    [Fact]
    public async Task The_composed_client_asks_the_step_up_its_host_registered()
    {
        var consent = new ScriptedConsent(UserConsentOutcome.Declined);
        var services = new ServiceCollection()
            .AddSyncClient(new Uri("https://sync.test"))
            .AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore())
            .AddSingleton(new DeviceStepUp(consent));
        await using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<DevicePairingClient>();
        var issued = await client.IssuePairingCodeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DeviceStepUp.DeclinedCode, issued.Error.Code);
        Assert.Single(consent.Prompts);
    }

    [Fact]
    public async Task A_host_with_no_step_up_mints_and_revokes_without_asking()
    {
        var (client, handler) = Client(consent: null);

        var code = await client.IssuePairingCodeAsync(TestContext.Current.CancellationToken);
        var revoked = await client.RevokeDeviceAsync(Phone, TestContext.Current.CancellationToken);

        Assert.True(code.IsSuccess);
        Assert.True(revoked.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
    }

    private static (DevicePairingClient Client, StubHttpMessageHandler Handler) Client(IUserConsent? consent, TimeProvider? clock = null)
    {
        var handler = new StubHttpMessageHandler((request, _) =>
            request.Method == HttpMethod.Delete
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : StubHttpMessageHandler.Json(HttpStatusCode.Created, """{"code":"K7MN9PQR","expiresAt":"2026-09-07T09:10:00+00:00"}"""));

        // Disposed with the handler's test: the client holds nothing past the call.
#pragma warning disable CA2000
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };
#pragma warning restore CA2000

        var client = new DevicePairingClient(
            http,
            new InMemoryDeviceCredentialStore(),
            stepUp: consent is null ? null : new DeviceStepUp(consent, clock));

        return (client, handler);
    }

    /// <summary>A prompt nobody answers until <see cref="Answer"/>: what an unattended desktop
    /// shows. Cancelling it answers Declined, as Windows Hello's own consent does.</summary>
    private sealed class UnansweredConsent : IUserConsent
    {
        private readonly TaskCompletionSource<UserConsentOutcome> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Shown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Answer(UserConsentOutcome outcome) => _answer.TrySetResult(outcome);

        public async Task<UserConsentOutcome> VerifyAsync(string message, CancellationToken cancellationToken = default)
        {
            Shown.TrySetResult();

            try
            {
                return await _answer.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return UserConsentOutcome.Declined;
            }
        }
    }

    /// <summary>A consent that answers <paramref name="outcome"/> and keeps every prompt it was shown.</summary>
    private sealed class ScriptedConsent(UserConsentOutcome outcome) : IUserConsent
    {
        public List<string> Prompts { get; } = [];

        public Task<UserConsentOutcome> VerifyAsync(string message, CancellationToken cancellationToken = default)
        {
            Prompts.Add(message);
            return Task.FromResult(outcome);
        }
    }
}
