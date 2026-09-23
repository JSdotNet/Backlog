extern alias DesktopHarness;

using System.Net;
using System.Text;
using System.Text.Json;

using Backlog.Desktop.UI.Mcp;
using Backlog.Desktop.UI.Shell;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// The harness's hook telemetry route, over HTTP, through the harness's own pipeline —
/// the arrangement <see cref="McpEndpointGateTests"/> covers for <c>/mcp</c>, for the
/// route that sits beside it behind the same switch and the same <c>Origin</c> check.
/// <para>
/// The telemetry port is replaced with a recording double, so a test event never
/// reaches the run files in this machine's profile.
/// </para>
/// </summary>
public class TelemetryEndpointGateTests
{
    private sealed class Harness(bool mcpServer, RecordingTelemetry telemetry) : WebApplicationFactory<DesktopHarness::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IAppFeatureSettings>(new FixedFeatures(mcpServer));
                services.AddSingleton<IDeliveryRunTelemetry>(telemetry);
            });
        }
    }

    private sealed class FixedFeatures(bool mcpServer) : IAppFeatureSettings
    {
        public event Action? Changed;

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "(replaced for this test)";

        public bool IsEnabled(string key) =>
            string.Equals(key, AppFeatures.McpServer, StringComparison.Ordinal) ? mcpServer : true;

        public string? SetEnabled(string key, bool enabled)
        {
            Changed?.Invoke();
            return null;
        }
    }

    private sealed class RecordingTelemetry : IDeliveryRunTelemetry
    {
        public List<string> Events { get; } = [];

        public Task RecordAsync(JsonElement hookEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(hookEvent.GetProperty("hook_event_name").GetString()!);
            return Task.CompletedTask;
        }
    }

    private static HttpRequestMessage Post(string body) =>
        new(HttpMethod.Post, BacklogTelemetryEndpoint.RoutePath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    [Fact]
    public async Task A_hook_event_is_taken_and_handed_to_the_telemetry_port()
    {
        var telemetry = new RecordingTelemetry();
        using var harness = new Harness(mcpServer: true, telemetry);
        using var client = harness.CreateClient();

        using var request = Post("""{"hook_event_name":"Stop","session_id":"s"}""");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["Stop"], telemetry.Events);
    }

    [Fact]
    public async Task A_body_that_is_not_a_json_object_is_refused()
    {
        var telemetry = new RecordingTelemetry();
        using var harness = new Harness(mcpServer: true, telemetry);
        using var client = harness.CreateClient();

        using var request = Post("[]");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(telemetry.Events);
    }

    /// <summary>The same switch as the tools: with the MCP server off the route is
    /// absent, not present and refusing.</summary>
    [Fact]
    public async Task The_route_is_absent_while_the_feature_is_off()
    {
        var telemetry = new RecordingTelemetry();
        using var harness = new Harness(mcpServer: false, telemetry);
        using var client = harness.CreateClient();

        using var request = Post("""{"hook_event_name":"Stop"}""");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(telemetry.Events);
    }

    [Fact]
    public async Task A_cross_origin_post_is_refused()
    {
        var telemetry = new RecordingTelemetry();
        using var harness = new Harness(mcpServer: true, telemetry);
        using var client = harness.CreateClient();

        using var request = Post("""{"hook_event_name":"Stop"}""");
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(telemetry.Events);
    }
}
