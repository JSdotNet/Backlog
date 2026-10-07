extern alias DesktopHarness;

using System.Net;
using System.Text;

using Backlog.Desktop.UI.Mcp;
using Backlog.Desktop.UI.Shell;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// The harness's hook telemetry route, over HTTP, through the harness's own pipeline —
/// the arrangement <see cref="McpEndpointGateTests"/> covers for <c>/mcp</c>, for the
/// route that sits beside it behind the same <c>Origin</c> check.
/// <para>
/// The telemetry port is replaced with a recording double, so a test event never
/// reaches the run files in this machine's profile.
/// </para>
/// </summary>
public class TelemetryEndpointGateTests
{
    private sealed class Harness(RecordingTelemetry telemetry) : IsolatedHarnessFactory<DesktopHarness::Program>
    {
        protected override void ConfigureHarness(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IAppFeatureSettings>(new AllFeaturesOn());
                services.AddSingleton<IDeliveryRunTelemetry>(telemetry);
            });
        }
    }

    /// <summary>Every key on, so what the harness serves does not depend on this
    /// worktree's own feature settings file.</summary>
    private sealed class AllFeaturesOn : IAppFeatureSettings
    {
        public event Action? Changed;

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "(replaced for this test)";

        public bool IsEnabled(string key) => true;

        public string? SetEnabled(string key, bool enabled)
        {
            Changed?.Invoke();
            return null;
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
        using var harness = new Harness(telemetry);
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
        using var harness = new Harness(telemetry);
        using var client = harness.CreateClient();

        using var request = Post("[]");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(telemetry.Events);
    }

    [Fact]
    public async Task A_cross_origin_post_is_refused()
    {
        var telemetry = new RecordingTelemetry();
        using var harness = new Harness(telemetry);
        using var client = harness.CreateClient();

        using var request = Post("""{"hook_event_name":"Stop"}""");
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(telemetry.Events);
    }
}
