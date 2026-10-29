extern alias DesktopHarness;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Backlog.Desktop.UI.Extensions;
using Backlog.Desktop.UI.Mcp;
using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.Mcp;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Backlog.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// The harness's MCP endpoint, over HTTP, through the harness's own pipeline.
/// <para>
/// Everything between <c>MapMcp</c> and a working endpoint is arrangement that no
/// unit test can see: whether the route survives sitting after
/// <c>MapRazorComponents</c>, whether <c>UseAntiforgery</c> rejects the POST
/// before it arrives, whether a context's feature gate is consulted per request
/// or was decided at startup. Those are the failures a green build and a green suite
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
    /// feature switch this class needs to decide.</summary>
    private sealed class Harness(
        bool sessions = true,
        IDeliverySurfaceLifecycle? surface = null,
        bool sessionRequests = true,
        ISessionRequestStore? queue = null)
        : IsolatedHarnessFactory<DesktopHarness::Program>
    {
        protected override void ConfigureHarness(IWebHostBuilder builder)
        {
            // After the harness's own registration, so this is the one resolved:
            // the last registration of a service wins.
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IAppFeatureSettings>(new FixedFeatures(sessions, sessionRequests));

                // A recording double rather than the real run store, which files
                // runs under this machine's shared state: a test run is not a run.
                if (surface is not null) services.AddSingleton(surface);

                // And an in-memory queue rather than the workspace's backlog.db.
                if (queue is not null) services.AddSingleton(queue);
            });
        }
    }

    /// <summary>Hand-rolled, because this repository uses no mocking library.
    /// Every key but the one under test answers true. There is no key for the
    /// endpoint itself — the MCP server is always on — so what is switchable here
    /// is a context's key, which decides which of its tools the endpoint lists.
    /// </summary>
    private sealed class FixedFeatures(bool sessions = true, bool sessionRequests = true) : IAppFeatureSettings
    {
        public event Action? Changed;

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "(replaced for this test)";

        public bool IsEnabled(string key) => key switch
        {
            _ when string.Equals(key, SessionFeatures.Sessions, StringComparison.Ordinal) => sessions,
            _ when string.Equals(key, SessionFeatures.SessionRequests, StringComparison.Ordinal) => sessionRequests,
            _ => true
        };

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
    /// The endpoint answers an <c>initialize</c>.
    /// <para>
    /// The assertion that matters is not the payload but that it got here at all:
    /// a 404 would mean the route never survived the pipeline, and a 400 would
    /// mean <c>UseAntiforgery</c> rejected the POST — both of which are invisible
    /// to every other test in this repository.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_endpoint_answers_an_initialize()
    {
        using var harness = new Harness();
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
        using var harness = new Harness();
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
        using var harness = new Harness();
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
        using var harness = new Harness();
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

    /// <summary>
    /// The delivery engine resolves a surface by matching operation names against
    /// the live tool list, so what this asserts is the whole of what makes
    /// Backlog bindable as one: the eight names of
    /// <c>delivery.surface.lifecycle@1</c>, over the real endpoint, in a
    /// <c>tools/list</c>.
    /// <para>
    /// The capability had been implemented and tested behind
    /// <c>IDeliverySurfaceLifecycle</c> for as long as the run store has existed,
    /// and none of that was reachable: with no tool under these names a flow
    /// found nothing, reported "no surface bound" — a normal outcome, which is
    /// why it was silent — and fell through to a dashboard. A unit test over the
    /// catalog would not have caught it either, because the catalog was not where
    /// it was missing.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_delivery_surface_is_listed_while_its_feature_is_on()
    {
        await using var harness = new Harness();
        using var client = harness.CreateClient();

        var body = await ListToolsAsync(client);

        Assert.All(
            DeliverySurfaceOperations.All,
            operation => Assert.Contains($"\"{operation}\"", body, StringComparison.Ordinal));
    }

    /// <summary>
    /// And the other half of local ADR 0012 §7: a group whose area is switched
    /// off is <em>absent</em> from the list rather than present and refusing. The
    /// surface shares the sessions key with <c>list_sessions</c>, so both go, and
    /// the tools of every other area stay — which is what tells a feature gate
    /// from a broken endpoint.
    /// </summary>
    [Fact]
    public async Task The_delivery_surface_is_absent_while_its_feature_is_off()
    {
        await using var harness = new Harness(sessions: false);
        using var client = harness.CreateClient();

        var body = await ListToolsAsync(client);

        Assert.All(
            DeliverySurfaceOperations.All,
            operation => Assert.DoesNotContain($"\"{operation}\"", body, StringComparison.Ordinal));

        Assert.DoesNotContain($"\"{BacklogMcpTools.Sessions.ToolNames.Single()}\"", body, StringComparison.Ordinal);

        // The endpoint is answering and the other areas are listed: the eight are
        // missing because the switch says so, not because nothing was published.
        Assert.All(
            BacklogMcpTools.Tracker.ToolNames,
            tool => Assert.Contains($"\"{tool}\"", body, StringComparison.Ordinal));
    }

    /// <summary>
    /// The session request queue's dispatcher tools are listed while
    /// <c>session-requests</c> is on (local ADR 0038 §5), through the same
    /// <c>ApplyFeatureGates</c> filter every group goes through.
    /// </summary>
    [Fact]
    public async Task The_dispatcher_tools_are_listed_while_session_requests_is_on()
    {
        await using var harness = new Harness();
        using var client = harness.CreateClient();

        var body = await ListToolsAsync(client);

        Assert.All(
            BacklogMcpTools.SessionRequests.ToolNames,
            tool => Assert.Contains($"\"{tool}\"", body, StringComparison.Ordinal));
    }

    /// <summary>And absent while it is off, with every other area still listed.</summary>
    [Fact]
    public async Task The_dispatcher_tools_are_absent_while_session_requests_is_off()
    {
        await using var harness = new Harness(sessionRequests: false);
        using var client = harness.CreateClient();

        var body = await ListToolsAsync(client);

        Assert.All(
            BacklogMcpTools.SessionRequests.ToolNames,
            tool => Assert.DoesNotContain($"\"{tool}\"", body, StringComparison.Ordinal));

        Assert.All(
            BacklogMcpTools.Sessions.ToolNames.Concat(BacklogMcpTools.Tracker.ToolNames),
            tool => Assert.Contains($"\"{tool}\"", body, StringComparison.Ordinal));
    }

    /// <summary>The harness maps the wait route too: the waiting request at once while
    /// the flag is on.</summary>
    [Fact]
    public async Task The_harness_wait_route_hands_out_the_waiting_request()
    {
        var queue = new InMemorySessionRequestStore();
        var request = queue.Add(SessionRequests.ForPlan("+session-dispatch", "JSdotNet/Backlog", @"D:\Repos\Backlog", DateTimeOffset.UtcNow));
        await using var harness = new Harness(queue: queue);
        using var client = harness.CreateClient();

        using var response = await client.GetAsync(SessionRequestWaitEndpoint.RoutePath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(request.Id.ToString("D"), body["id"]!.GetValue<string>());
        Assert.Equal("/backlog-tools:execute-plan +session-dispatch", body["brief"]!.GetValue<string>());
    }

    /// <summary>404 while <c>session-requests</c> is off, without asking the queue.</summary>
    [Fact]
    public async Task The_harness_wait_route_is_not_found_while_session_requests_is_off()
    {
        var queue = new InMemorySessionRequestStore();
        await using var harness = new Harness(sessionRequests: false, queue: queue);
        using var client = harness.CreateClient();

        using var response = await client.GetAsync(SessionRequestWaitEndpoint.RoutePath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, queue.NextCalls);
    }

    /// <summary>And behind the harness's loopback Origin check, as <c>/mcp</c> is.</summary>
    [Fact]
    public async Task The_harness_wait_route_refuses_a_cross_origin_call()
    {
        var queue = new InMemorySessionRequestStore();
        await using var harness = new Harness(queue: queue);
        using var client = harness.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, SessionRequestWaitEndpoint.RoutePath);
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, queue.NextCalls);
    }

    /// <summary>The harness serves the question hook script beside <c>/mcp</c>, byte for
    /// byte what the app embeds and installs (local ADR 0041).</summary>
    [Fact]
    public async Task The_harness_serves_the_question_hook_script_as_embedded()
    {
        await using var harness = new Harness();
        using var client = harness.CreateClient();

        using var response = await client.GetAsync(AskBacklogHook.RoutePath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            AskBacklogHook.Bytes.ToArray(),
            await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>And behind the same loopback Origin check.</summary>
    [Fact]
    public async Task The_harness_hook_script_route_refuses_a_cross_origin_call()
    {
        await using var harness = new Harness();
        using var client = harness.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, AskBacklogHook.RoutePath);
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// <c>start_run</c> with its stages sent as the JSON text of the list, as an
    /// execute-plan worker sent them: the run starts, with the stages the text
    /// encodes. Over the real endpoint, because what decides this is the order of
    /// the host's filters and the SDK's binding, and neither is visible below it.
    /// </summary>
    [Fact]
    public async Task Start_run_takes_its_stages_as_a_JSON_encoded_string()
    {
        var surface = new RecordingSurface();
        await using var harness = new Harness(surface: surface);
        using var client = harness.CreateClient();

        var body = await CallToolAsync(client, DeliverySurfaceOperations.StartRun, new
        {
            worktree = @"D:\Repos\Backlog\.claude\worktrees\example",
            skillId = "flow-code",
            title = "Stages as a string",
            stages = """["Update Base","Scope","Implement"]""",
            // Not a start_run parameter: the worker sent it, and it is ignored.
            resume = "false"
        });

        Assert.DoesNotContain("\"isError\":true", body, StringComparison.Ordinal);
        Assert.Contains("run-1", body, StringComparison.Ordinal);
        Assert.Equal(["Update Base", "Scope", "Implement"], surface.Stages);
    }

    /// <summary>
    /// <c>start_run</c> with no worktree is refused naming the worktree, rather than
    /// with "An error occurred invoking 'start_run'." — which is all the SDK says
    /// about a binding failure, and all that worker was told.
    /// </summary>
    [Fact]
    public async Task Start_run_without_a_worktree_is_refused_naming_it()
    {
        var surface = new RecordingSurface();
        await using var harness = new Harness(surface: surface);
        using var client = harness.CreateClient();

        var body = await CallToolAsync(client, DeliverySurfaceOperations.StartRun, new
        {
            skillId = "flow-code",
            title = "No worktree",
            stages = new[] { "Scope" }
        });

        Assert.Contains("\"isError\":true", body, StringComparison.Ordinal);
        Assert.Contains("start_run needs worktree", body, StringComparison.Ordinal);
        Assert.Null(surface.Stages);
    }

    /// <summary>
    /// The feature gate answers before the arguments are read: a switched-off
    /// <c>start_run</c> is an unknown tool, not one asking for its worktree — which
    /// would describe a tool the gate exists to deny.
    /// </summary>
    [Fact]
    public async Task A_switched_off_start_run_is_unknown_before_its_arguments_are_read()
    {
        await using var harness = new Harness(sessions: false, surface: new RecordingSurface());
        using var client = harness.CreateClient();

        var body = await CallToolAsync(client, DeliverySurfaceOperations.StartRun, new { skillId = "flow-code" });

        Assert.Contains("Unknown tool", body, StringComparison.Ordinal);
        Assert.DoesNotContain("needs worktree", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>open_window</c> is behind no flag (local ADR 0042 §7): listed with an area
    /// switched off, and answered end to end through the SDK and both filters. In
    /// the harness, <c>desktop</c> says no window was opened and still
    /// carries the <c>backlog://</c> link; an id the catalog does not offer is not
    /// available.
    /// </summary>
    [Fact]
    public async Task Open_window_is_listed_and_answers_in_the_harness()
    {
        await using var harness = new Harness(sessions: false);
        using var client = harness.CreateClient();

        var list = await ListToolsAsync(client);
        Assert.Contains("\"open_window\"", list, StringComparison.Ordinal);

        var desktop = await CallToolAsync(client, "open_window", new { surface = "tasks", target = "desktop", plan = "+shell-pop-out-windows" });
        Assert.Contains("No window was opened for Tasks", desktop, StringComparison.Ordinal);
        Assert.Contains("backlog://window/tasks?plan=shell-pop-out-windows", desktop, StringComparison.Ordinal);

        var proposals = await CallToolAsync(client, "open_window", new { surface = "proposals", target = "web" });
        Assert.Contains("is not available", proposals, StringComparison.Ordinal);
        Assert.Contains("\"isError\":true", proposals, StringComparison.Ordinal);
    }

    /// <summary>One stateless <c>tools/call</c>, as its body.</summary>
    private static async Task<string> CallToolAsync(HttpClient client, string name, object arguments)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BacklogMcpServerRegistration.EndpointPath)
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new { name, arguments }
            })
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(
            response.IsSuccessStatusCode,
            $"{name} answered {(int)response.StatusCode} {response.StatusCode}. Body:\n{body}");

        return body;
    }

    /// <summary>Records the stages <c>start_run</c> reached the port with, and
    /// answers every other operation with nothing much.</summary>
    private sealed class RecordingSurface : IDeliverySurfaceLifecycle
    {
        public IReadOnlyList<string>? Stages { get; private set; }

        public Task<DeliverySurfaceOpened> OpenDashboardAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeliverySurfaceOpened(DeliverySurfaceActivation.Available, "Recording."));

        public Task<DeliveryRunStarted> StartRunAsync(
            string worktree,
            string skillId,
            string title,
            IReadOnlyList<string> stages,
            string? changeKind = null,
            string? sessionId = null,
            string? trigger = null,
            string? schedule = null,
            string? repository = null,
            CancellationToken cancellationToken = default)
        {
            Stages = stages;
            return Task.FromResult(new DeliveryRunStarted("run-1", Resumed: false, SessionTitle: title));
        }

        public Task RecordPromptAsync(string worktree, string runId, string prompt, string? kind = null, string? label = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetRunContextAsync(string worktree, string runId, string? changeKind = null, string? approval = null, string? approvalNote = null, string? model = null, JsonObject? runContext = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<DeliveryStageUpdated> UpdateStageAsync(
            string worktree,
            string runId,
            int stageIndex,
            string status,
            string? output = null,
            IReadOnlyList<DeliveryStageLink>? links = null,
            IReadOnlyList<DeliveryScenario>? scenarios = null,
            DeliveryMonitoring? monitoring = null,
            JsonObject? execution = null,
            IReadOnlyList<DeliveryScenarioEvidence>? evidence = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeliveryStageUpdated(runId, stageIndex, status, DoneCount: 0, SessionTitle: null));

        public Task FinishRunAsync(string worktree, string runId, string status, string? summary = null, IReadOnlyList<DeliverySyncUnitVerdict>? verdicts = null, JsonObject? quality = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<DeliveryRun>> ListRunsAsync(string worktree, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeliveryRun>>([]);

        public Task<DeliveryRun?> GetRunAsync(string worktree, string runId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DeliveryRun?>(null);
    }

    /// <summary>A stateless <c>tools/list</c>, as its body. The transport this
    /// harness maps is stateless, so there is no session to establish
    /// first.</summary>
    private static async Task<string> ListToolsAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BacklogMcpServerRegistration.EndpointPath)
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" })
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(
            response.IsSuccessStatusCode,
            $"tools/list answered {(int)response.StatusCode} {response.StatusCode}. Body:\n{body}");

        return body;
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

        // Every gated group: open_window has no flag (local ADR 0042 §7) and stays.
        Assert.All(
            BacklogMcpTools.Groups.Where(group => group.FeatureKey is not null),
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
