using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>
    /// Development, because this host has no Cosmos configured and only a
    /// Development run is allowed to fall back to the in-memory replicas — see
    /// <c>CosmosReplicaRegistration</c>. Nothing these tests assert is
    /// environment-specific: the authentication pipeline, the authorization
    /// policies and the problem shape are the same either way. The two cases
    /// that are about a deployed host say so themselves.
    /// </summary>
    internal string Environment { get; init; } = Environments.Development;

    internal string? ConfiguredSigningKey { get; init; } = SigningKey;

    /// <summary>A test's own registrations, applied after the service has made
    /// all of its own. That ordering is the point: <c>ConfigureTestServices</c>
    /// runs last, so a RemoveAll plus a re-add wins wherever the service
    /// happened to register the thing being replaced.
    /// <para>
    /// Not called <c>Services</c>: <see cref="WebApplicationFactory{TEntryPoint}"/>
    /// already has a property by that name holding the built provider, and
    /// shadowing it would make <c>factory.Services</c> mean two different things
    /// depending on the static type in front of it.
    /// </para></summary>
    internal Action<IServiceCollection>? TestServices { get; init; }

    /// <summary>One more setting, for a case that needs the host configured
    /// rather than its services replaced — the deployed shape, where what is
    /// missing is the thing under test and everything else has to be
    /// present.</summary>
    internal (string Key, string? Value)? Configuration { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        builder.UseSetting($"{SyncTokenOptions.SectionName}:SigningKey", ConfiguredSigningKey);

        if (Configuration is { } setting)
        {
            builder.UseSetting(setting.Key, setting.Value);
        }

        if (TestServices is not null)
        {
            builder.ConfigureTestServices(TestServices);
        }
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
