extern alias DesktopHarness;

using System.Net.Http.Headers;
using System.Net.Http.Json;

using Backlog.Desktop.UI.Extensions;
using Backlog.Desktop.UI.Mcp;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Mcp;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// The scope policies of local ADR 0052 §4 over the real endpoint: a call to a tool runs only
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

    private const string TestGrant = "Test grant (g_test0000000000000)";

    /// <summary>One tool per scope that serves a tool, with the arguments its allowed call
    /// takes. <c>telemetry:write</c> serves a route, and <c>inbox:capture</c> and
    /// <c>agents:grant</c> nothing yet;
    /// McpScopePoliciesTests holds those.</summary>
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

    /// <summary>A scope as the JSON body spells it inside the message: in escaped apostrophes.</summary>
    private static string Escaped(string scope) => @"\u0027" + scope + @"\u0027";

    private static Task<string> CallToolAsync(HttpClient client, string name, IReadOnlyList<string> scopes) =>
        CallToolAsync(client, name, scopes, name == "open_window" ? new { surface = "tasks", target = "desktop" } : new { });

    private static async Task<string> CallToolAsync(HttpClient client, string name, IReadOnlyList<string>? scopes, object arguments)
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

        if (scopes is not null) request.Headers.Add(ScopesHeader, string.Join(' ', scopes));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"{name} answered {(int)response.StatusCode}. Body:\n{body}");

        return body;
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
