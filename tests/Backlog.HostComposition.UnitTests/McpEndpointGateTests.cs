extern alias DesktopHarness;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Backlog.Desktop.UI.Extensions;
using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.Mcp;
using Backlog.SharedKernel;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// The harness's MCP endpoint, over HTTP, through the harness's own pipeline.
/// <para>
/// Everything between <c>MapMcp</c> and a working endpoint is arrangement that no
/// unit test can see: whether the route survives sitting after
/// <c>MapRazorComponents</c>, whether <c>UseAntiforgery</c> rejects the POST
/// before it arrives, whether the feature gate is consulted per request or was
/// decided at startup. Those are the failures a green build and a green suite
/// would both sit on top of, and this is the cheapest place to find them —
/// in-process, with no Aspire run and no port.
/// </para>
/// <para>
/// The feature switches are replaced with a double rather than flipped on the
/// real store. The harness keeps its own under
/// <c>obj/local-development/feature.settings.json</c>, which is this worktree's
/// and is the file a QA session's harness reads: a test that turned a feature on
/// and left it on would be changing what somebody else's run starts from.
/// </para>
/// </summary>
public class McpEndpointGateTests
{
    /// <summary>The error code every unknown-repository answer carries. Spelled
    /// out rather than read off the implementation, which is internal to the tool
    /// library — and which is the right way round anyway: this is what a session
    /// sees on the wire.</summary>
    private const string UnknownRepository = "repository.not_found";


    /// <summary>Development for the reason <see cref="WebHarnessHostTests"/>
    /// gives — it is the only environment where the provider validates — plus the
    /// feature switches this class needs to decide.</summary>
    private sealed class Harness(bool mcpServer) : WebApplicationFactory<DesktopHarness::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            // After the harness's own registration, so this is the one resolved:
            // the last registration of a service wins.
            builder.ConfigureServices(services =>
                services.AddSingleton<IAppFeatureSettings>(new FixedFeatures(mcpServer)));
        }
    }

    /// <summary>Hand-rolled, because this repository uses no mocking library.
    /// Every key but the one under test answers true, so a gate that read the
    /// wrong key would let the request through and fail the first test
    /// here.</summary>
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

    /// <summary>An <c>initialize</c>, which is the first thing any MCP client
    /// sends and the only call that needs no session behind it.</summary>
    private static HttpRequestMessage Initialize(string protocolVersion = "2024-11-05")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BacklogMcpServerRegistration.EndpointPath)
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion,
                    capabilities = new { },
                    clientInfo = new { name = "host-composition-tests", version = "1.0.0" }
                }
            })
        };

        // Streamable HTTP lets the server answer with either, and a client that
        // accepts only one of them is refused before anything is dispatched.
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        return request;
    }

    /// <summary>
    /// With the feature off the endpoint is <em>absent</em>, not present and
    /// refusing — which is what
    /// <c>08-crosscutting-concepts.md#feature-enablement</c> asks of every
    /// switchable capability, and what local ADR 0012 §7 repeats for these tools.
    /// </summary>
    [Fact]
    public async Task The_endpoint_is_absent_while_the_feature_is_off()
    {
        using var harness = new Harness(mcpServer: false);
        using var client = harness.CreateClient();

        using var request = Initialize();
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// And with it on the endpoint answers an <c>initialize</c>.
    /// <para>
    /// The assertion that matters is not the payload but that it got here at all:
    /// a 404 would mean the route never survived the pipeline, and a 400 would
    /// mean <c>UseAntiforgery</c> rejected the POST — both of which are invisible
    /// to every other test in this repository.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_endpoint_answers_an_initialize_while_the_feature_is_on()
    {
        using var harness = new Harness(mcpServer: true);
        using var client = harness.CreateClient();

        using var request = Initialize();
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(
            response.IsSuccessStatusCode,
            $"POST {BacklogMcpServerRegistration.EndpointPath} answered {(int)response.StatusCode} "
            + $"{response.StatusCode}. 404 means the route did not survive the pipeline; 400 means "
            + $"antiforgery rejected it. Body:\n{body}");

        // The server identifies itself, which is what proves an MCP server rather
        // than something else answered on the path.
        Assert.Contains("serverInfo", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The harness checks the <c>Origin</c> and asks for no token, and the two
    /// halves of that are a decision rather than an oversight.
    /// <para>
    /// <b>The check is here because the tools are the same tools and the
    /// workspace under them is the real one.</b> This harness composes
    /// <c>%LOCALAPPDATA%\Backlog.Debug</c>, whose repositories point their devbook
    /// folders at clones on this machine — so a browser page reaching this
    /// endpoint reaches a person's backlog and a person's repository, not a
    /// fixture. QA has to turn the feature on to test it, and a harness left
    /// running afterwards is reachable from any page on the machine by DNS
    /// rebinding; Aspire's port being dynamic is a number a page can find by
    /// trying, not a secret. It costs QA nothing, which is the other half of the
    /// decision: a test client is not a browser and sends no <c>Origin</c> at all,
    /// as the two tests above show by passing without one.
    /// </para>
    /// <para>
    /// <b>The token is not here</b>, and that stays true: it is a secret a person
    /// copies into a registration, and here it would be one QA had to fetch out of
    /// a container to drive a test.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_harness_refuses_a_cross_origin_request_and_asks_for_no_token()
    {
        using var harness = new Harness(mcpServer: true);
        using var client = harness.CreateClient();

        using var request = Initialize();
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // And no token was ever the reason: the same call with no Origin, and no
        // Authorization header either, is answered.
        using var withoutOrigin = Initialize();
        using var answered = await client.SendAsync(withoutOrigin, TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Forbidden, answered.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, answered.StatusCode);
    }

    /// <summary>A loopback page is not a cross-origin caller. The storybook and
    /// the harness itself are served from one, and the check is about the open
    /// web.</summary>
    [Fact]
    public async Task The_harness_answers_a_loopback_origin()
    {
        using var harness = new Harness(mcpServer: true);
        using var client = harness.CreateClient();

        using var request = Initialize();
        request.Headers.Add("Origin", "http://localhost:5001");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.True(
            response.IsSuccessStatusCode,
            $"A loopback Origin was answered {(int)response.StatusCode} {response.StatusCode}.");
    }

    /// <summary>
    /// Two tool calls in a row, on whatever the transport gives them, which is the
    /// thing no unit test in this change set could see.
    /// <para>
    /// <b>What it is checking.</b> The SDK constructs each
    /// <c>[McpServerToolType]</c> class per invocation out of
    /// <c>request.Services</c>. If that provider were ever the <em>first</em>
    /// request's scope — the scope ASP.NET Core disposes when that request ends —
    /// the second call would throw <c>ObjectDisposedException</c> resolving
    /// <c>ITaskItems</c>, and every client in the product would work exactly once.
    /// The bridge in <c>McpServerWorker</c> rests on the same property, one
    /// container further out.
    /// </para>
    /// <para>
    /// <b>What the transport actually does here.</b> <c>WithHttpTransport()</c> on
    /// SDK 2.2.0 defaults to <em>stateless</em> — SEP-2567 removed
    /// <c>Mcp-Session-Id</c> in the <c>2026-07-28</c> revision and the default
    /// followed it — so no session is minted, every request gets a fresh server
    /// context and a fresh scope, and the hazard above cannot arise as posed. This
    /// does not assume that: it carries the session id when one is offered and
    /// makes the two calls either way, so it keeps proving the same thing if the
    /// transport is ever configured <c>Stateful</c>.
    /// </para>
    /// <para>
    /// Asserted on a tool that answers without depending on what this machine has
    /// registered: an unknown repository is a refusal the tool composes on
    /// purpose, and it can only be composed by a tool instance that was built and
    /// ran. A disposed scope fails earlier and differently, which is what the last
    /// assertion is for.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Two_tool_calls_in_a_row_both_answer()
    {
        using var harness = new Harness(mcpServer: true);
        using var client = harness.CreateClient();

        // The last revision that has the initialize handshake and a session in
        // it, so a stateful server would hand one back here.
        using var handshake = Initialize("2025-06-18");
        using var initialized = await client.SendAsync(handshake, TestContext.Current.CancellationToken);

        Assert.True(initialized.IsSuccessStatusCode);

        var session = initialized.Headers.TryGetValues("mcp-session-id", out var ids) ? ids.Single() : null;

        using var ready = Notification(session, "notifications/initialized");
        using var acknowledged = await client.SendAsync(ready, TestContext.Current.CancellationToken);

        Assert.True(acknowledged.IsSuccessStatusCode);

        var first = await CallAsync(client, session, id: 2);
        var second = await CallAsync(client, session, id: 3);

        // The tool's own refusal, composed inside the tool. Both times, on one
        // session, out of two different request scopes.
        Assert.Contains(UnknownRepository, first, StringComparison.Ordinal);
        Assert.Contains(UnknownRepository, second, StringComparison.Ordinal);

        // And named, because this is the failure the test exists for: a second
        // call resolving out of the first request's scope says so in these words.
        Assert.DoesNotContain("ObjectDisposedException", second, StringComparison.Ordinal);
        Assert.DoesNotContain("disposed", second, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One <c>tools/call</c> on an established session, as its body.
    /// <c>list_sessions</c> with a repository nobody has registered: a refusal the
    /// tool composes itself, which is only reachable if the tool was constructed
    /// and run.</summary>
    private static async Task<string> CallAsync(HttpClient client, string? session, int id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BacklogMcpServerRegistration.EndpointPath)
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id,
                method = "tools/call",
                @params = new
                {
                    name = BacklogMcpTools.Sessions.ToolNames.Single(),
                    arguments = new { repository = "JSdotNet/NoSuchRepositoryInThisTest" }
                }
            })
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (session is not null) request.Headers.Add("mcp-session-id", session);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(
            response.IsSuccessStatusCode,
            $"Call {id} on session '{session ?? "(stateless)"}' answered {(int)response.StatusCode} "
            + $"{response.StatusCode}. Body:\n{body}");

        return body;
    }

    /// <summary>The <c>initialized</c> notification. It carries no id, so the
    /// server answers 202 and nothing else.</summary>
    private static HttpRequestMessage Notification(string? session, string method)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BacklogMcpServerRegistration.EndpointPath)
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", method })
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (session is not null) request.Headers.Add("mcp-session-id", session);

        return request;
    }

    /// <summary>
    /// A switched-off group is absent from <c>tools/list</c> rather than present
    /// and refusing. The gate above is the whole server; this is the per-group
    /// one, and it is the half that has to be read per request — a client holding
    /// a list from before a flip will still call what it was told about.
    /// </summary>
    [Fact]
    public void The_exposure_predicate_hides_a_group_whose_feature_is_off()
    {
        var everythingOff = new AllOff();

        Assert.All(
            BacklogMcpTools.Groups,
            group => Assert.All(
                group.ToolNames,
                name => Assert.False(BacklogMcpTools.IsExposed(name, everythingOff))));
    }

    /// <summary>Every key off, so the list-tools filter has nothing left to
    /// show.</summary>
    private sealed class AllOff : IAppFeatureSettings
    {
        public event Action? Changed;

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "(replaced for this test)";

        public bool IsEnabled(string key) => false;

        public string? SetEnabled(string key, bool enabled)
        {
            Changed?.Invoke();
            return null;
        }
    }
}
