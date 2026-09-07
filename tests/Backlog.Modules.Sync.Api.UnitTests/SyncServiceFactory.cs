using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// The sync service in a test host, with a signing key the test controls so a
/// token can be forged, expired, or signed with the wrong key on purpose.
/// </summary>
internal sealed class SyncServiceFactory : WebApplicationFactory<Program>
{
    /// <summary>A fixed key, so a test can also sign a token itself and see the
    /// service accept it. Fixed rather than random because a test that fails
    /// should fail the same way twice.
    /// <para>
    /// Sixty-four bytes rather than the thirty-two the service insists on, so
    /// that a test can sign HS512 with this very key and watch the service
    /// refuse it anyway. HS512 needs a 512-bit key to sign at all, and a case
    /// that could not be built would leave the pinned algorithm list untested.
    /// </para></summary>
    internal static string SigningKey { get; } = Convert.ToBase64String(Enumerable.Range(0, 64).Select(i => (byte)i).ToArray());

    /// <summary>A different 32-byte key, for the token this service must
    /// refuse.</summary>
    internal static string OtherSigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    internal string Environment { get; init; } = "Production";

    internal string? ConfiguredSigningKey { get; init; } = SigningKey;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        builder.UseSetting($"{SyncTokenOptions.SectionName}:SigningKey", ConfiguredSigningKey);
    }
}

/// <summary>
/// Walking a device through registration and pairing over HTTP, so a test can
/// say "given two paired devices" in one line.
/// </summary>
internal static class SyncClientExtensions
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    internal static async Task<DeviceRegistrationResponse> RegisterDevice(this HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RegisterDevice), new RegisterDeviceRequest(name), Cancellation);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!;
    }

    internal static async Task<string> DeviceToken(this HttpClient client, DeviceRegistrationResponse device)
    {
        var response = await client.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.DeviceToken),
            new DeviceTokenRequest(device.DeviceId, device.Credential),
            Cancellation);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<DeviceTokenResponse>(Cancellation))!.AccessToken;
    }

    /// <summary>Registers a device and comes back holding its bearer token.</summary>
    internal static async Task<HttpClient> RegisteredDevice(this HttpClient client, string name)
    {
        var device = await client.RegisterDevice(name);
        client.Bearing(await client.DeviceToken(device));
        return client;
    }

    internal static HttpClient Bearing(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
