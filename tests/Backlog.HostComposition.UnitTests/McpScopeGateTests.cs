extern alias DesktopHarness;

using System.Net.Http.Headers;
using System.Net.Http.Json;

using Backlog.Desktop.UI.Extensions;
using Backlog.Desktop.UI.Mcp;
using Backlog.Infrastructure.Mcp;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Mcp;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// The scope policies of local ADR 0058 §4 over the real endpoint: a call to a tool runs only
/// for a caller holding the tool's scope, the audit names that caller, and a switched-off group
/// is still unknown whatever the caller holds.
/// <para>
/// The harness asks for no token, so a test puts the principal on the request itself, ahead of
/// the harness's pipeline, from a header only this factory reads. The desktop listener puts a
/// grant's principal there the same way after validating its token; the filter that decides is
/// the one both hosts install.
/// </para>
/// <para>
/// <b>An allowed call is one the scope did not refuse.</b> Each is sent with no arguments, so
/// it ends at the argument binding or the tool's own refusal — after the scope filter, and
/// before anything is written.
/// </para>
/// </summary>
public sealed class McpScopeGateTests
{
    private const string ScopesHeader = "X-Test-Scopes";

    /// <summary>Puts the tokenless caller of local ADR 0059 on the request, holding the
    /// header's scopes, the way the desktop listener does for a call with no
    /// <c>Authorization</c> header while the Unauthenticated row is open.</summary>
    private const string UnauthenticatedHeader = "X-Test-Unauthenticated";

    private const string TestGrant = "Test grant (g_test0000000000000)";

    /// <summary>One tool per scope that serves a tool. <c>telemetry:write</c> serves a route
    /// alone, which McpScopePoliciesTests and McpServerWorkerTests hold.</summary>
    public static TheoryData<string, string> ToolPerScope => new()
    {
        { McpScopes.TasksRead, "find_item" },
        { McpScopes.TasksWrite, "comment" },
        { McpScopes.TasksAdmin, "delete_goal" },
        { McpScopes.DevbookRead, "devbook_get_state" },
        { McpScopes.DevbookWrite, "devbook_post_note" },
        { McpScopes.RunsWrite, "link_session" },
        { McpScopes.NotifyAsk, "get_answer" },
        { McpScopes.UiOpen, "open_window" },
        { McpScopes.SessionsDispatch, "mark_session_request" },
        { McpScopes.InboxCapture, "capture_to_inbox" },
        { McpScopes.AgentsGrant, "list_agent_requests" },
    };

    [Theory]
    [MemberData(nameof(ToolPerScope))]
    public async Task A_caller_holding_the_scope_reaches_the_tool(string scope, string tool)
    {
        var audit = new RecordingAuditLog();
        await using var harness = new Harness(audit);
        using var client = harness.CreateClient();

        var body = await CallToolAsync(client, tool, [scope]);

        Assert.DoesNotContain("needs the scope", body, StringComparison.Ordinal);
        var call = Assert.Single(audit.Records);
        Assert.Equal(tool, call.Tool);
        Assert.Equal(TestGrant, call.Caller);
    }

    [Theory]
    [MemberData(nameof(ToolPerScope))]
    public async Task A_caller_without_the_scope_is_refused_and_the_refusal_names_the_grant(string scope, string tool)
    {
        var audit = new RecordingAuditLog();
        await using var harness = new Harness(audit);
        using var client = harness.CreateClient();

        // Every other grantable scope: only the one the tool needs is missing.
        var body = await CallToolAsync(client, tool, [.. McpScopes.Grantable.Where(other => other != scope)]);

        // The message as the JSON body carries it, apostrophes escaped.
        Assert.Contains("needs the scope " + Escaped(scope), body, StringComparison.Ordinal);
        var call = Assert.Single(audit.Records);
        Assert.Equal(tool, call.Tool);
        Assert.Equal(McpCallOutcome.Refused, call.Outcome);
        Assert.Equal(TestGrant, call.Caller);
    }

    [Fact]
    public async Task A_switched_off_group_is_unknown_even_to_a_caller_holding_its_scope()
    {
        await using var harness = new Harness(new RecordingAuditLog(), sessions: false);
        using var client = harness.CreateClient();

        var body = await CallToolAsync(client, DeliverySurfaceOperations.GetRun, [McpScopes.RunsWrite]);

        Assert.Contains("Unknown tool", body, StringComparison.Ordinal);
        Assert.DoesNotContain("needs the scope", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_switched_off_group_is_unknown_before_the_scope_is_asked()
    {
        await using var harness = new Harness(new RecordingAuditLog(), sessions: false);
        using var client = harness.CreateClient();

        var body = await CallToolAsync(client, DeliverySurfaceOperations.GetRun, []);

        Assert.Contains("Unknown tool", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_harness_caller_holds_every_grantable_scope()
    {
        var audit = new RecordingAuditLog();
        await using var harness = new Harness(audit);
        using var client = harness.CreateClient();

        // No test principal: the harness's own.
        var body = await CallToolAsync(client, "open_window", null, new { surface = "tasks", target = "desktop" });

        Assert.DoesNotContain("\"isError\":true", body, StringComparison.Ordinal);
        Assert.Equal(McpAudit.HarnessCaller, Assert.Single(audit.Records).Caller);
    }

    /// <summary>A tokenless caller's <c>tools/list</c> names only the tools its scopes reach,
    /// and never <c>request_access</c> (local ADR 0059 §3).</summary>
    [Fact]
    public async Task The_unauthenticated_caller_lists_only_the_tools_its_scopes_reach()
    {
        await using var harness = new Harness(new RecordingAuditLog());
        using var client = harness.CreateClient();

        var names = await ListToolsAsync(client, UnauthenticatedHeader, [McpScopes.TasksRead]);

        Assert.Contains("find_item", names);
        Assert.Contains("list_entries", names);
        Assert.DoesNotContain("comment", names);
        Assert.DoesNotContain("delete_goal", names);
        Assert.DoesNotContain("devbook_get_state", names);
        Assert.DoesNotContain("request_access", names);
        Assert.All(names, name => Assert.Equal(McpScopes.TasksRead, McpToolScopes.ScopeOf(name)));
    }

    /// <summary>The filter is the tokenless caller's alone: a grant holding one scope and the
    /// harness's own caller still list every tool the feature switches expose, as before.</summary>
    [Fact]
    public async Task A_grant_and_the_harness_still_list_every_exposed_tool()
    {
        await using var harness = new Harness(new RecordingAuditLog());
        using var client = harness.CreateClient();

        var everything = await ListToolsAsync(client, header: null, scopes: null);
        var grant = await ListToolsAsync(client, ScopesHeader, [McpScopes.TasksRead]);

        Assert.Contains("request_access", everything);
        Assert.Contains("comment", everything);
        Assert.Contains("delete_goal", everything);
        Assert.Equal(everything, grant);
    }

    /// <summary>A tokenless call outside the row's scopes is refused in-band - a JSON-RPC
    /// answer, never a 401 that would send the client to OAuth - and the audit names the caller
    /// <c>unauthenticated</c> (local ADR 0059 §3, §5).</summary>
    [Fact]
    public async Task A_tokenless_call_outside_its_scopes_is_refused_in_band_and_audited_as_unauthenticated()
    {
        var audit = new RecordingAuditLog();
        await using var harness = new Harness(audit);
        using var client = harness.CreateClient();

        var refused = await CallToolAsync(client, "comment", UnauthenticatedHeader, [McpScopes.TasksRead], new { });
        var admitted = await CallToolAsync(client, "find_item", UnauthenticatedHeader, [McpScopes.TasksRead], new { });

        Assert.Contains("needs the scope " + Escaped(McpScopes.TasksWrite), refused, StringComparison.Ordinal);
        Assert.DoesNotContain("needs the scope", admitted, StringComparison.Ordinal);
        Assert.Collection(
            audit.Records,
            record =>
            {
                Assert.Equal("comment", record.Tool);
                Assert.Equal(McpCallOutcome.Refused, record.Outcome);
                Assert.Equal(McpAudit.UnauthenticatedCaller, record.Caller);
            },
            record =>
            {
                Assert.Equal("find_item", record.Tool);
                Assert.Equal(McpAudit.UnauthenticatedCaller, record.Caller);
            });
    }

    /// <summary><c>request_access</c> asks on behalf of a grant, and a tokenless caller has none:
    /// it is refused to it, in-band like any other refused call and never with a 401, and the
    /// audit names the caller <c>unauthenticated</c> (local ADR 0059 §3).</summary>
    [Fact]
    public async Task Request_access_is_refused_to_a_tokenless_caller_in_band_and_audited()
    {
        var audit = new RecordingAuditLog();
        await using var harness = new Harness(audit);
        using var client = harness.CreateClient();

        var body = await CallToolAsync(client, "request_access", UnauthenticatedHeader, [.. McpScopes.UnauthenticatedOfferable], new { });

        Assert.Contains("needs a grant token", body, StringComparison.Ordinal);
        var call = Assert.Single(audit.Records);
        Assert.Equal("request_access", call.Tool);
        Assert.Equal(McpCallOutcome.Refused, call.Outcome);
        Assert.Equal(McpAudit.UnauthenticatedCaller, call.Caller);
    }

    /// <summary>A grant holding only a single-purpose scope - the Question hook's
    /// <c>notify:ask</c> - calls no tool, <c>request_access</c> included: the refusal is
    /// in-band, says how to ask instead, and the audit names the grant (local ADR 0024).</summary>
    [Fact]
    public async Task Request_access_is_refused_to_a_grant_holding_only_a_single_purpose_scope()
    {
        var audit = new RecordingAuditLog();
        await using var harness = new Harness(audit);
        using var client = harness.CreateClient();

        var body = await CallToolAsync(client, "request_access", [McpScopes.NotifyAsk]);

        Assert.Contains("needs a grant that holds a scope beyond telemetry:write and notify:ask", body, StringComparison.Ordinal);
        Assert.Contains("headersHelper --request", body, StringComparison.Ordinal);
        var call = Assert.Single(audit.Records);
        Assert.Equal("request_access", call.Tool);
        Assert.Equal(McpCallOutcome.Refused, call.Outcome);
        Assert.Equal(TestGrant, call.Caller);
    }

    /// <summary>A scope as the JSON body spells it inside the message: in escaped apostrophes.</summary>
    private static string Escaped(string scope) => @"\u0027" + scope + @"\u0027";

    private static Task<string> CallToolAsync(HttpClient client, string name, IReadOnlyList<string> scopes) =>
        CallToolAsync(client, name, scopes, name == "open_window" ? new { surface = "tasks", target = "desktop" } : new { });

    private static Task<string> CallToolAsync(HttpClient client, string name, IReadOnlyList<string>? scopes, object arguments) =>
        CallToolAsync(client, name, scopes is null ? null : ScopesHeader, scopes, arguments);

    private static async Task<string> CallToolAsync(HttpClient client, string name, string? header, IReadOnlyList<string>? scopes, object arguments)
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

        if (header is not null && scopes is not null) request.Headers.Add(header, string.Join(' ', scopes));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"{name} answered {(int)response.StatusCode}. Body:\n{body}");

        return body;
    }

    /// <summary>The tool names a <c>tools/list</c> answers, sorted, for the caller the header
    /// puts on the request, or the harness's own with none.</summary>
    private static async Task<IReadOnlyList<string>> ListToolsAsync(HttpClient client, string? header, IReadOnlyList<string>? scopes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BacklogMcpServerRegistration.EndpointPath)
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list", @params = new { } })
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (header is not null && scopes is not null) request.Headers.Add(header, string.Join(' ', scopes));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"tools/list answered {(int)response.StatusCode}. Body:\n{body}");

        // A streamable HTTP answer may come as one SSE event: its data line is the JSON.
        var json = body.TrimStart().StartsWith('{')
            ? body
            : body.Split('\n').Single(line => line.StartsWith("data:", StringComparison.Ordinal))["data:".Length..];

        using var document = System.Text.Json.JsonDocument.Parse(json);
        return [.. document.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()!)
            .Order(StringComparer.Ordinal)];
    }

    private sealed class Harness(IMcpAuditLog audit, bool sessions = true) : IsolatedHarnessFactory<DesktopHarness::Program>
    {
        protected override void ConfigureHarness(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IAppFeatureSettings>(new Features(sessions));
                services.AddSingleton(audit);
                services.AddSingleton<IStartupFilter, TestPrincipal>();
            });
        }
    }

    /// <summary>Puts a principal holding the header's scopes on the request, first in the
    /// pipeline, the way the desktop listener puts a grant's on after its token checks out.</summary>
    private sealed class TestPrincipal : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (HttpContext context, RequestDelegate inner) =>
            {
                if (context.Request.Headers.TryGetValue(ScopesHeader, out var scopes))
                {
                    context.User = McpScopePolicies.Static(
                        TestGrant,
                        scopes.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
                }
                else if (context.Request.Headers.TryGetValue(UnauthenticatedHeader, out var opened))
                {
                    context.User = McpScopePolicies.Unauthenticated(
                        opened.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
                }

                await inner(context);
            });

            next(app);
        };
    }

    private sealed class Features(bool sessions) : IAppFeatureSettings
    {
        public event Action? Changed;

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "(replaced for this test)";

        public bool IsEnabled(string key) =>
            sessions || !string.Equals(key, SessionFeatures.Sessions, StringComparison.Ordinal);

        public string? SetEnabled(string key, bool enabled)
        {
            Changed?.Invoke();
            return null;
        }
    }

    private sealed class RecordingAuditLog : IMcpAuditLog
    {
        private readonly List<McpAuditRecord> _records = [];

        public event Action? Changed;

        public IReadOnlyList<McpAuditRecord> Records
        {
            get
            {
                lock (_records) return [.. _records];
            }
        }

        public Task AppendAsync(McpAuditRecord record, CancellationToken cancellationToken = default)
        {
            lock (_records) _records.Add(record);
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<McpAuditRecord>> LatestAsync(int count, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<McpAuditRecord>>([.. Records.Reverse().Take(count)]);
    }
}
