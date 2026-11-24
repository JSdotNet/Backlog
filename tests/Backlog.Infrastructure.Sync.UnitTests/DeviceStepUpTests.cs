using System.Net;

using Backlog.Infrastructure.Sync.Extensions;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// Windows Hello before a pairing code is minted or a device revoked (local ADR 0058 §6): with a
/// <see cref="DeviceStepUp"/> composed, each call reaches the service only once the person
/// confirms; cancelled or unavailable, nothing is sent and the failure says which. Without one —
/// the web harness and the phone — the client asks nothing.
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
        Assert.Equal("K7MN9PQR", code.Unwrap().Code);
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
    public async Task Refusing_a_pairing_revokes_the_device_without_asking()
    {
        var consent = new ScriptedConsent(UserConsentOutcome.Declined);
        var (client, handler) = Client(consent);

        var refused = await client.RefuseDeviceAsync(Phone, TestContext.Current.CancellationToken);

        Assert.True(refused.IsSuccess);
        Assert.Equal(HttpMethod.Delete, Assert.Single(handler.Requests).Method);
        Assert.Empty(consent.Prompts);
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

    private static (DevicePairingClient Client, StubHttpMessageHandler Handler) Client(IUserConsent? consent)
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
            stepUp: consent is null ? null : new DeviceStepUp(consent));

        return (client, handler);
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
