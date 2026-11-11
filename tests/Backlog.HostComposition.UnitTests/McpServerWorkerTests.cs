using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;

using Backlog.Desktop.Mcp;
using Backlog.Desktop.UI.Mcp;
using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Backlog.Tests;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// The desktop head's MCP listener, held to the one thing its constructor and a
/// port change must not do: build an ASP.NET host on the thread that asked.
/// <para>
/// That thread is the one about to draw the first window, or the Settings screen
/// a person just typed a port into. Building the listener composes a whole
/// <c>WebApplication</c> — Kestrel, routing, the MCP server and the bridge into
/// the head's container — so it belongs on the chain that binds it.
/// </para>
/// <para>
/// The proof is a head container that will not answer until the test lets it:
/// building the listener asks it which ports it composes, so a build on the
/// calling thread would hold that thread until the gate opened.
/// </para>
/// </summary>
public sealed class McpServerWorkerTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-worker-tests-" + Guid.NewGuid().ToString("N"));

    private readonly GatedProvider _services = new(new ServiceCollection().BuildServiceProvider());

    [Fact]
    public async Task Constructing_the_worker_returns_before_the_listener_is_built()
    {
        var settings = Settings(FreePort());
        _services.Close();

        var construct = Task.Run(() => new McpServerWorker(_services, settings), TestContext.Current.CancellationToken);
        var returned = await Returned(construct);

        _services.Open();
        Assert.True(returned, "The constructor waited on the head's container, so it built the listener in place.");

        using var worker = await construct;
        await ListeningAsync(worker, settings.McpServerPort);

        Assert.True(_services.Asked > 0);
    }

    [Fact]
    public async Task A_port_change_returns_before_the_new_listener_is_built()
    {
        var settings = Settings(FreePort());
        using var worker = new McpServerWorker(_services, settings);
        await ListeningAsync(worker, settings.McpServerPort);

        var moved = FreePort();
        _services.Close();

        var change = Task.Run(() => settings.SetMcpServerPort(moved), TestContext.Current.CancellationToken);
        var returned = await Returned(change);

        _services.Open();
        Assert.True(returned, "The port change waited on the head's container, so it built the listener in place.");
        Assert.Null(await change);

        await ListeningAsync(worker, moved);
    }

    /// <summary>A port change the window closes on top of is still given back:
    /// the listener built after disposal is let go rather than bound.</summary>
    [Fact]
    public async Task A_listener_built_after_disposal_is_never_bound()
    {
        var port = FreePort();
        var settings = Settings(port);
        _services.Close();

        var worker = new McpServerWorker(_services, settings);

        // Disposed while the build is still held at the gate, and only then let
        // through: the order is fixed, so the build always finds itself superseded.
        // Dispose gives up on the stuck chain after its own drain timeout.
        await Task.Run(worker.Dispose, TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken);
        _services.Open();

        Assert.False(worker.IsListening);
        Assert.Null(worker.Address);

        using var probe = new TcpListener(IPAddress.Loopback, port);
        probe.Start();
    }

    /// <summary>A port somebody else held at start is reported, not retried on its
    /// own (local ADR 0012 §1) — and once it is free, a Retry from the Settings
    /// page binds the same configured port without the app being restarted.</summary>
    [Fact]
    public async Task Retry_after_a_failed_bind_binds_the_configured_port_once_it_is_free()
    {
        var port = FreePort();
        var settings = Settings(port);

        McpServerWorker worker;
        using (var squatter = Occupy(port))
        {
            worker = new McpServerWorker(_services, settings);
            await FailedAsync(worker);
        }

        using (worker)
        {
            Assert.False(worker.IsListening);
            Assert.Null(worker.Address);

            worker.Retry();

            await ListeningAsync(worker, port);
        }
    }

    /// <summary>A Retry while the port is still held fails the same way, with the
    /// same sentence, rather than trying another port.</summary>
    [Fact]
    public async Task Retry_while_the_port_is_still_held_reports_the_collision_again()
    {
        var port = FreePort();
        var settings = Settings(port);
        using var squatter = Occupy(port);

        using var worker = new McpServerWorker(_services, settings);
        var first = await FailedAsync(worker);

        // Read inside the Changed that Retry raises on this thread before it
        // returns, rather than after it: by then the chain may already have
        // failed again. Cleared there, so a page can say it is trying rather than
        // go on showing the last failure as if nothing happened.
        var caller = Environment.CurrentManagedThreadId;
        var raised = false;
        string? seen = null;
        void OnRetried()
        {
            if (raised || Environment.CurrentManagedThreadId != caller) return;

            raised = true;
            seen = worker.LastError;
        }

        worker.Changed += OnRetried;
        try
        {
            worker.Retry();
        }
        finally
        {
            worker.Changed -= OnRetried;
        }

        Assert.True(raised, "Retry did not say it had queued an attempt.");
        Assert.Null(seen);

        var again = await FailedAsync(worker);

        Assert.Equal(first, again);
        Assert.Contains($"Port {port} is already in use", again, StringComparison.Ordinal);
        Assert.False(worker.IsListening);
    }

    /// <summary>Nothing to retry once the window has closed: a disposed worker
    /// never binds again, so the port stays free.</summary>
    [Fact]
    public async Task Retry_after_disposal_binds_nothing()
    {
        var port = FreePort();
        var settings = Settings(port);

        var worker = new McpServerWorker(_services, settings);
        await ListeningAsync(worker, port);
        worker.Dispose();

        worker.Retry();
        await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);

        Assert.False(worker.IsListening);
        using var probe = new TcpListener(IPAddress.Loopback, port);
        probe.Start();
    }

    /// <summary>A Retry while the listener holds its port leaves it alone: the
    /// session a client has open on it is not dropped for a click.</summary>
    [Fact]
    public async Task Retry_while_listening_keeps_the_running_listener()
    {
        var settings = Settings(FreePort());
        using var worker = new McpServerWorker(_services, settings);
        await ListeningAsync(worker, settings.McpServerPort);

        // Closed: a rebuild would ask the head's container and stall on it.
        _services.Close();
        var asked = _services.Asked;

        worker.Retry();
        await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);

        _services.Open();
        Assert.Equal(asked, _services.Asked);
        Assert.True(worker.IsListening);
        Assert.Null(worker.LastError);
    }

    /// <summary>
    /// Claude Code's OTLP logs reach the head's listener at <c>/v1/logs</c> behind the
    /// telemetry token (local ADR 0024): the real listener, bound on loopback, refuses an
    /// export with no token, the wrong token, the MCP server's own token and the question
    /// hook's — storing nothing — and stores the one carrying the telemetry token the
    /// Settings page copies.
    /// </summary>
    [Fact]
    public async Task The_logs_route_refuses_a_missing_or_wrong_token_and_stores_the_right_ones_export()
    {
        var store = new RecordingRequestStore();
        var services = new GatedProvider(new ServiceCollection()
            .AddSingleton<IClaudeApiRequestStore>(store)
            .BuildServiceProvider());
        var settings = Settings(FreePort());

        using var worker = new McpServerWorker(services, settings);
        await ListeningAsync(worker, settings.McpServerPort);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{settings.McpServerPort}") };
        var token = settings.EnsureTelemetryToken();

        using (var response = await client.SendAsync(Export(null), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        }

        using (var response = await client.SendAsync(Export(token + "-wrong"), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var response = await client.SendAsync(Export(settings.EnsureMcpServerToken()), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var response = await client.SendAsync(Export(settings.EnsureQuestionHookToken()), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.Empty(store.Added);

        using (var response = await client.SendAsync(Export(token), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(["req_011CYfixture0000000000001", "req_011CYfixture0000000000002"], store.Added.Select(request => request.RequestId));
    }

    /// <summary>
    /// A port the head composed as a singleton is the head's own instance, and the
    /// listener never disposes it. The logs route resolves the request store out of
    /// the request's scope; forwarded through a scoped factory, that scope's end
    /// disposed the desktop singleton with it, and the main window went on writing to
    /// a disposed store. Handed over as the instance, two exports land in it and it
    /// is still undisposed after the request scopes that resolved it have ended.
    /// </summary>
    [Fact]
    public async Task Ending_a_listener_scope_leaves_a_forwarded_desktop_singleton_undisposed()
    {
        var store = new DisposableRequestStore();
        var collection = new ServiceCollection().AddSingleton<IClaudeApiRequestStore>(store);
        collection.AddSingleton(new DesktopServiceRegistrations([.. collection]));
        var services = new GatedProvider(collection.BuildServiceProvider());
        var settings = Settings(FreePort());

        using var worker = new McpServerWorker(services, settings);
        await ListeningAsync(worker, settings.McpServerPort);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{settings.McpServerPort}") };
        var token = settings.EnsureTelemetryToken();

        for (var export = 0; export < 2; export++)
        {
            using var response = await client.SendAsync(Export(token), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // A scope ends after its response has gone, so the disposal it would carry is
        // given a moment to arrive rather than read off the response.
        var disposed = await Task.WhenAny(store.Disposed, Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken)) == store.Disposed;

        Assert.False(disposed, "Ending a listener request's scope disposed the desktop app's singleton.");
        Assert.Equal(4, store.Added.Count);
    }

    /// <summary>A request store that says when it is disposed.</summary>
    private sealed class DisposableRequestStore : IClaudeApiRequestStore, IDisposable
    {
        private readonly RecordingRequestStore _inner = new();
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Disposed => _disposed.Task;

        public IReadOnlyList<ClaudeApiRequest> Added => _inner.Added;

        public Task<int> AddAsync(IReadOnlyList<ClaudeApiRequest> requests, CancellationToken cancellationToken = default) =>
            _inner.AddAsync(requests, cancellationToken);

        public Task<IReadOnlyList<ClaudeApiRequest>> ListAsync(string? sessionId = null, CancellationToken cancellationToken = default) =>
            _inner.ListAsync(sessionId, cancellationToken);

        public void Dispose() => _disposed.TrySetResult();
    }

    /// <summary>
    /// The session-request long poll reaches the head's listener behind the same bearer
    /// token as <c>/mcp</c> (local ADR 0038 §4): the real listener refuses a wait with no
    /// token and one with the wrong token, and answers the right one with the waiting
    /// request at once.
    /// </summary>
    [Fact]
    public async Task The_session_request_wait_refuses_a_missing_or_wrong_token_and_answers_the_right_one()
    {
        var queue = new InMemorySessionRequestStore();
        var request = queue.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", DateTimeOffset.UtcNow));
        var services = new GatedProvider(new ServiceCollection()
            .AddSingleton<ISessionRequestStore>(queue)
            .AddSingleton<IAppFeatureSettings>(new SessionRequestsOn())
            .BuildServiceProvider());
        var settings = Settings(FreePort());

        using var worker = new McpServerWorker(services, settings);
        await ListeningAsync(worker, settings.McpServerPort);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{settings.McpServerPort}") };
        var token = settings.EnsureMcpServerToken();

        using (var response = await client.SendAsync(Wait(null), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        }

        using (var response = await client.SendAsync(Wait(token + "-wrong"), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.Equal(0, queue.NextCalls);

        using (var response = await client.SendAsync(Wait(token), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains(request.Id.ToString("D"), await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
    }

    private static HttpRequestMessage Wait(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, SessionRequestWaitEndpoint.RoutePath);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return request;
    }

    /// <summary>Every key on, <c>session-requests</c> among them.</summary>
    private sealed class SessionRequestsOn : IAppFeatureSettings
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

    /// <summary>
    /// The telemetry token reaches <c>/v1/logs</c> and nothing else: on the real listener a
    /// tool call carrying it is refused with 401 before any tool runs, and so are the
    /// session-request wait and the hook script route.
    /// </summary>
    [Fact]
    public async Task A_telemetry_token_calling_a_tool_is_refused_with_401()
    {
        var settings = Settings(FreePort());
        using var worker = new McpServerWorker(_services, settings);
        await ListeningAsync(worker, settings.McpServerPort);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{settings.McpServerPort}") };
        var token = settings.EnsureTelemetryToken();

        foreach (var tool in new[] { "notify", "list_entries", "transition" })
        {
            using var response = await client.SendAsync(ToolCall(tool, token), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        }

        foreach (var path in new[] { SessionRequestWaitEndpoint.RoutePath, AskBacklogHook.RoutePath })
        {
            using var get = new HttpRequestMessage(HttpMethod.Get, path);
            get.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(get, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    /// <summary>
    /// The question hook's token reaches the question tools alone: a call to notify, ask,
    /// get_answer or close gets past the guard, while any other tool, a tool list, a
    /// batch and the session-request wait answer 401.
    /// </summary>
    [Fact]
    public async Task A_question_hook_token_reaches_the_question_tools_and_nothing_else()
    {
        var settings = Settings(FreePort());
        using var worker = new McpServerWorker(_services, settings);
        await ListeningAsync(worker, settings.McpServerPort);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{settings.McpServerPort}") };
        var token = settings.EnsureQuestionHookToken();

        foreach (var tool in McpLoopbackGuard.QuestionTools)
        {
            // Answered by the MCP server itself, with the call's id: the guard read the body
            // and put it back, so MapMcp read the same request from the start.
            using var response = await client.SendAsync(ToolCall(tool, token), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("\"id\":1", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }

        using (var smuggled = Rpc("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"ask\",\"Name\":\"list_entries\",\"arguments\":{}}}", token))
        using (var response = await client.SendAsync(smuggled, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        foreach (var tool in new[] { "list_entries", "transition", "claim_delivery", "devbook_approve" })
        {
            using var response = await client.SendAsync(ToolCall(tool, token), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var list = Rpc("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}", token))
        using (var response = await client.SendAsync(list, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var batch = Rpc("[" + Call("ask") + "]", token))
        using (var response = await client.SendAsync(batch, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var response = await client.SendAsync(Wait(token), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    /// <summary>
    /// The MCP server's own token still calls every tool: the split narrows the two new
    /// tokens and leaves the registrations' token as it was everywhere but the logs route.
    /// </summary>
    [Fact]
    public async Task The_mcp_token_still_reaches_every_tool()
    {
        var settings = Settings(FreePort());
        using var worker = new McpServerWorker(_services, settings);
        await ListeningAsync(worker, settings.McpServerPort);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{settings.McpServerPort}") };

        using var response = await client.SendAsync(ToolCall("list_entries", settings.EnsureMcpServerToken()), TestContext.Current.CancellationToken);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A grant token the broker minted (local ADR 0052 §3) gets past the guard wherever the MCP
    /// token does, and not onto the logs route; once its grant is revoked the same token is
    /// refused with 401 and <c>WWW-Authenticate: Bearer</c> on the very next request.
    /// </summary>
    [Fact]
    public async Task A_grant_token_reaches_what_the_mcp_token_does_until_its_grant_is_revoked()
    {
        var grants = new FileMcpGrantStore(Path.Combine(_root, "grants-" + Guid.NewGuid().ToString("N"), FileMcpGrantStore.FileName));
        var tokens = new McpGrantTokens(grants, TimeProvider.System);
        var services = new GatedProvider(new ServiceCollection().AddSingleton(tokens).BuildServiceProvider());
        var settings = Settings(FreePort());

        using var worker = new McpServerWorker(services, settings);
        await ListeningAsync(worker, settings.McpServerPort);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{settings.McpServerPort}") };
        var grant = grants.Create("Claude Code", ["tasks:read"], "jobsc");
        var token = tokens.Mint(grant.Id).Token!.Value;

        using (var response = await client.SendAsync(Rpc("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", token), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var response = await client.SendAsync(Export(token), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        grants.Revoke(grant.Id);

        using (var response = await client.SendAsync(Rpc("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", token), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        }
    }

    /// <summary>A grant store that cannot be read — a locked or denied <c>mcp-grants.json</c> —
    /// refuses the grant token with 401, so the headersHelper runs again, and never answers a
    /// 500.</summary>
    [Fact]
    public async Task A_grant_store_that_cannot_be_read_refuses_with_401_not_500()
    {
        var grants = new FileMcpGrantStore(Path.Combine(_root, "grants-" + Guid.NewGuid().ToString("N"), FileMcpGrantStore.FileName));
        var store = new FailableMcpGrantStore(grants);
        var tokens = new McpGrantTokens(store, TimeProvider.System);
        var services = new GatedProvider(new ServiceCollection().AddSingleton(tokens).BuildServiceProvider());
        var settings = Settings(FreePort());

        using var worker = new McpServerWorker(services, settings);
        await ListeningAsync(worker, settings.McpServerPort);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{settings.McpServerPort}") };
        var token = tokens.Mint(grants.Create("Claude Code", [], "jobsc").Id).Token!.Value;
        store.Failure = new UnauthorizedAccessException("Access to the path is denied.");

        using var response = await client.SendAsync(Rpc("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", token), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    /// <summary>A listener whose head composed no grant tokens — the web harness's case — has
    /// no grant path: a JWT-shaped bearer is refused like any other unknown token.</summary>
    [Fact]
    public async Task Without_grant_tokens_a_JWT_shaped_bearer_is_refused()
    {
        var settings = Settings(FreePort());
        using var worker = new McpServerWorker(_services, settings);
        await ListeningAsync(worker, settings.McpServerPort);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{settings.McpServerPort}") };
        var grants = new FileMcpGrantStore(Path.Combine(_root, "grants-" + Guid.NewGuid().ToString("N"), FileMcpGrantStore.FileName));
        var token = new McpGrantTokens(grants, TimeProvider.System).Mint(grants.Create("x", [], "jobsc").Id).Token!.Value;

        using var response = await client.SendAsync(Rpc("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", token), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>One JSON-RPC <c>tools/call</c> of <paramref name="tool"/>.</summary>
    private static string Call(string tool) =>
        "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"" + tool + "\",\"arguments\":{}}}";

    /// <summary>A <c>tools/call</c> of <paramref name="tool"/> on <c>/mcp</c>, as the hook
    /// script sends one.</summary>
    private static HttpRequestMessage ToolCall(string tool, string token) => Rpc(Call(tool), token);

    private static HttpRequestMessage Rpc(string body, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, McpServerWorker.EndpointPath)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>The JSON fixture <c>ClaudeCodeOtlpLogsTests</c> describes, posted the
    /// way the exporter posts it, with <paramref name="token"/> as its bearer header.</summary>
    private static HttpRequestMessage Export(string? token)
    {
        var content = new ByteArrayContent(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "claude-code-otlp", "claude-code-logs.json")));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, ClaudeCodeLogsEndpoint.RoutePath) { Content = content };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return request;
    }

    /// <summary>Keeps every request the route stored, in order.</summary>
    private sealed class RecordingRequestStore : IClaudeApiRequestStore
    {
        private readonly List<ClaudeApiRequest> _added = [];

        public IReadOnlyList<ClaudeApiRequest> Added
        {
            get
            {
                lock (_added) return [.. _added];
            }
        }

        public Task<int> AddAsync(IReadOnlyList<ClaudeApiRequest> requests, CancellationToken cancellationToken = default)
        {
            lock (_added) _added.AddRange(requests);
            return Task.FromResult(requests.Count);
        }

        public Task<IReadOnlyList<ClaudeApiRequest>> ListAsync(string? sessionId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ClaudeApiRequest>>([.. Added.Where(request => sessionId is null || request.SessionId == sessionId)]);
    }

    public void Dispose()
    {
        _services.Open();

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private WorkspaceSettingsStore Settings(int port)
    {
        var settings = new WorkspaceSettingsStore(Path.Combine(_root, "store-" + Guid.NewGuid().ToString("N")));
        Assert.Null(settings.SetMcpServerPort(port));
        return settings;
    }

    private static async Task ListeningAsync(McpServerWorker worker, int port)
    {
        var deadline = DateTime.UtcNow + Patience;
        var changed = new SemaphoreSlim(0);
        worker.Changed += () => changed.Release();

        while (!(worker.IsListening && worker.Address?.Port == port))
        {
            Assert.Null(worker.LastError);
            var left = deadline - DateTime.UtcNow;
            Assert.True(left > TimeSpan.Zero, $"The listener never came up on {port}.");
            await changed.WaitAsync(left < TimeSpan.FromMilliseconds(200) ? left : TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Waits until the worker reports why it is not listening, and answers
    /// that sentence.</summary>
    internal static async Task<string> FailedAsync(McpServerWorker worker)
    {
        var deadline = DateTime.UtcNow + Patience;
        var changed = new SemaphoreSlim(0);
        void OnChanged() => changed.Release();
        worker.Changed += OnChanged;

        try
        {
            while (true)
            {
                if (worker.LastError is { } error) return error;

                Assert.False(worker.IsListening, "The listener bound a port the test was holding.");
                var left = deadline - DateTime.UtcNow;
                Assert.True(left > TimeSpan.Zero, "The listener never reported its failed bind.");
                await changed.WaitAsync(left < TimeSpan.FromMilliseconds(200) ? left : TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            worker.Changed -= OnChanged;
        }
    }

    /// <summary>Holds <paramref name="port"/> on loopback the way another program
    /// would, until disposed.</summary>
    internal static TcpListener Occupy(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        return listener;
    }

    /// <summary>Whether <paramref name="call"/> came back while the head's container
    /// was still refusing to answer.</summary>
    private static async Task<bool> Returned(Task call) =>
        await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)) == call;

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>The head's container, answering only while the gate is open.</summary>
    private sealed class GatedProvider(IServiceProvider inner) : IServiceProvider
    {
        private readonly ManualResetEventSlim _gate = new(initialState: true);
        private int _asked;

        public int Asked => Volatile.Read(ref _asked);

        public void Close() => _gate.Reset();

        public void Open() => _gate.Set();

        public object? GetService(Type serviceType)
        {
            Interlocked.Increment(ref _asked);
            _gate.Wait(Patience);
            return inner.GetService(serviceType);
        }
    }
}
