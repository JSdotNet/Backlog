using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Backlog.Modules.Sync.Abstractions;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// A device whose app is older than the sync service sends nothing and says
/// why. The service's version is read from its anonymous root; the request that
/// would have gone out is answered here, on the device, with the same
/// ProblemDetails shape every screen already reads a sync failure from.
/// </summary>
public sealed class SyncCompatibilityHandlerTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_newer_service_is_sent_nothing_and_the_answer_asks_for_an_update()
    {
        using var host = Host.Create(app: "0.1.122.0", service: _ => "0.2.0");

        using var response = await host.Client.PostAsync("/api/sync/tasks", new StringContent("{}"), Cancellation);

        Assert.Equal((HttpStatusCode)426, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal(SyncErrorCodes.ClientOutdated, problem.GetProperty("code").GetString());
        Assert.Equal(
            "The sync service is newer than this version of Backlog. Update Backlog to keep syncing.",
            problem.GetProperty("detail").GetString());

        // Only the question; the push itself never left the device.
        var asked = Assert.Single(host.Wire.Requests);
        Assert.Equal("/", asked.Path);
        Assert.True(host.Compatibility.UpdateRequired);
    }

    [Theory]
    [InlineData("0.1.0")]
    [InlineData("0.1.122")]
    public async Task A_service_no_newer_than_the_app_gets_the_request(string service)
    {
        using var host = Host.Create(app: "0.1.122.0", service: _ => service);

        using var response = await host.Client.PostAsync("/api/sync/tasks", new StringContent("{}"), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["/", "/api/sync/tasks"], host.Wire.Requests.Select(request => request.Path));
        Assert.False(host.Compatibility.UpdateRequired);
    }

    [Fact]
    public async Task The_service_is_asked_once_for_every_request_inside_the_window()
    {
        using var host = Host.Create(app: "0.1.122", service: _ => "0.1.0");

        using (await host.Client.GetAsync("/api/sync/inbox", Cancellation)) { }
        host.Clock.Advance(SyncCompatibility.VerdictWindow - TimeSpan.FromSeconds(1));
        using (await host.Client.GetAsync("/api/sync/inbox", Cancellation)) { }

        Assert.Single(host.Wire.Requests, request => request.Path == "/");
    }

    /// <summary>A service rolled back to a version this app matches lets it sync
    /// again on its own, without a restart.</summary>
    [Fact]
    public async Task After_the_window_the_service_is_asked_again()
    {
        var deployed = "0.2.0";
        using var host = Host.Create(app: "0.1.122", service: _ => deployed);

        using (var blocked = await host.Client.GetAsync("/api/sync/inbox", Cancellation))
        {
            Assert.Equal((HttpStatusCode)426, blocked.StatusCode);
        }

        deployed = "0.1.0";
        host.Clock.Advance(SyncCompatibility.VerdictWindow);

        using var resumed = await host.Client.GetAsync("/api/sync/inbox", Cancellation);

        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.False(host.Compatibility.UpdateRequired);
    }

    [Fact]
    public async Task A_change_of_verdict_is_announced()
    {
        using var host = Host.Create(app: "0.1.122", service: _ => "0.2.0");
        var raised = 0;
        host.Compatibility.Changed += () => raised++;

        using (await host.Client.GetAsync("/api/sync/inbox", Cancellation)) { }
        host.Clock.Advance(SyncCompatibility.VerdictWindow);
        using (await host.Client.GetAsync("/api/sync/inbox", Cancellation)) { }

        // Blocked once; the second check reached the same verdict.
        Assert.Equal(1, raised);
    }

    /// <summary>A root that does not answer, or answers without a readable
    /// version, says nothing about compatibility; sync goes on as before.</summary>
    [Theory]
    [InlineData("unavailable")]
    [InlineData("throws")]
    [InlineData("no-version")]
    [InlineData("not-json")]
    [InlineData("unreadable-version")]
    public async Task A_service_whose_version_cannot_be_read_gets_the_request(string root)
    {
        using var host = Host.Create(app: "0.1.122", root: root switch
        {
            "unavailable" => () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            "throws" => () => throw new HttpRequestException("No route to host."),
            "no-version" => () => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"service":"Backlog Sync"}"""),
            "not-json" => () => StubHttpMessageHandler.Json(HttpStatusCode.OK, "<html></html>"),
            _ => () => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"version":"next"}""")
        });

        using var response = await host.Client.GetAsync("/api/sync/inbox", Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(host.Compatibility.UpdateRequired);
    }

    private sealed class Host : IDisposable
    {
        private Host(HttpClient client, StubHttpMessageHandler wire, SyncCompatibility compatibility, FakeTimeProvider clock)
        {
            Client = client;
            Wire = wire;
            Compatibility = compatibility;
            Clock = clock;
        }

        public HttpClient Client { get; }

        public StubHttpMessageHandler Wire { get; }

        public SyncCompatibility Compatibility { get; }

        public FakeTimeProvider Clock { get; }

        /// <param name="service">The version the root reports.</param>
        /// <param name="root">The root's whole answer, for the cases where it
        /// reports no version at all.</param>
        public static Host Create(
            string app,
            Func<int, string>? service = null,
            Func<HttpResponseMessage>? root = null)
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-20T09:00:00Z", CultureInfo.InvariantCulture));

            var wire = new StubHttpMessageHandler((request, index) =>
                request.RequestUri?.AbsolutePath == "/"
                    ? root?.Invoke() ?? StubHttpMessageHandler.Json(
                        HttpStatusCode.OK,
                        $$"""{"service":"Backlog Sync","role":"thin sync layer","version":"{{service!(index)}}","commit":""}""")
                    : StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]"));

            var compatibility = new SyncCompatibility(new SyncAppVersion(app), clock);
            var client = new HttpClient(new SyncCompatibilityHandler(compatibility) { InnerHandler = wire })
            {
                BaseAddress = new Uri("https://sync.test")
            };

            return new Host(client, wire, compatibility, clock);
        }

        public void Dispose() => Client.Dispose();
    }
}
