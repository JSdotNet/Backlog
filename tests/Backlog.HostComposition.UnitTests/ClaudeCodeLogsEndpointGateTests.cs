extern alias DesktopHarness;

using System.Net;
using System.Net.Http.Headers;

using Backlog.Desktop.UI.Mcp;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// The ingest test: a captured Claude Code OTLP export replayed over HTTP at the
/// harness's <c>/v1/logs</c>, through the harness's own pipeline and composition, into
/// the <c>backlog.db</c> of the test's own workspace (local ADR 0024).
/// <para>
/// Nothing is replaced but the workspace: the route, the reader and the SQLite store
/// are the ones the harness composes, so what is asserted is what lands in the app's
/// local database. The fixtures are described on <c>ClaudeCodeOtlpLogsTests</c>.
/// </para>
/// </summary>
public class ClaudeCodeLogsEndpointGateTests
{
    private sealed class Harness : IsolatedHarnessFactory<DesktopHarness::Program>
    {
        protected override void ConfigureHarness(IWebHostBuilder builder) =>
            builder.ConfigureServices(services => services.AddSingleton<IAppFeatureSettings>(new AllFeaturesOn()));
    }

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

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpRequestMessage Replay(string fixture, string contentType)
    {
        var content = new ByteArrayContent(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "claude-code-otlp", fixture)));
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        return new HttpRequestMessage(HttpMethod.Post, ClaudeCodeLogsEndpoint.RoutePath) { Content = content };
    }

    [Theory]
    [InlineData("claude-code-logs.json", "application/json")]
    [InlineData("claude-code-logs.pb", "application/x-protobuf")]
    public async Task A_captured_export_lands_its_api_requests_in_the_local_database_once(string fixture, string contentType)
    {
        using var harness = new Harness();
        using var client = harness.CreateClient();

        using (var first = Replay(fixture, contentType))
        using (var response = await client.SendAsync(first, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        }

        // The exporter's retry of a batch it never saw acknowledged.
        using (var retry = Replay(fixture, contentType))
        using (var response = await client.SendAsync(retry, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var stored = await harness.Services.GetRequiredService<IClaudeApiRequestStore>().ListAsync(cancellationToken: Ct);

        Assert.Equal(["req_011CYfixture0000000000001", "req_011CYfixture0000000000002"], stored.Select(r => r.RequestId));

        var second = stored[1];
        Assert.Equal("ae81a408-527a-4a84-b181-d77fdda707e1", second.SessionId);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 22, 51, 16, 236, TimeSpan.Zero), second.Timestamp);
        Assert.Equal("claude-opus-5-5", second.Model);
        Assert.Equal("high", second.Effort);
        Assert.Equal(193755, second.CostUsdMicros);
        Assert.Equal(3, second.InputTokens);
        Assert.Equal(1420, second.OutputTokens);
        Assert.Equal(40211, second.CacheReadTokens);
        Assert.Equal(15876, second.CacheCreationTokens);
        Assert.Equal(9120, second.DurationMs);
        Assert.Equal("agent:custom", second.QuerySource);
        Assert.Equal("Explore", second.AgentName);
        Assert.Equal("delivery:phase-review", second.SkillName);
        Assert.Equal("ca2e2b77-95d0-496d-ad14-0f3704fcbda7", second.PromptId);
    }

    [Fact]
    public async Task A_cross_origin_post_is_refused_and_nothing_is_stored()
    {
        using var harness = new Harness();
        using var client = harness.CreateClient();

        using var request = Replay("claude-code-logs.json", "application/json");
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await harness.Services.GetRequiredService<IClaudeApiRequestStore>().ListAsync(cancellationToken: Ct));
    }
}
