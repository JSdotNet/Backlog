extern alias DesktopHarness;

using System.Net.Http.Headers;
using System.Net.Http.Json;

using Backlog.Desktop.UI.Extensions;
using Backlog.Desktop.UI.Mcp;
using Backlog.Infrastructure.Mcp;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Mcp;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// Windows Hello before a <c>tasks:admin</c> tool runs (local ADR 0052 §6), over the real
/// endpoint: the call waits for the person, runs when they confirm, and is refused — with
/// nothing changed and the refusal audited — when they cancel or nothing can ask. A
/// <c>tasks:write</c> tool, <c>transition</c> to Done among them, asks nothing, and neither
/// does the web harness's own caller (§9).
/// <para>
/// A grant's principal is put on the request from a header, as <see cref="McpScopeGateTests"/>
/// does, and the harness's <see cref="IUserConsent"/> is replaced by one that answers as told.
/// </para>
/// </summary>
public sealed class McpStepUpTests
{
    private const string ScopesHeader = "X-Test-Scopes";

    private const string TestGrant = "Test grant (g_test0000000000000)";

    /// <summary>Every tool <c>tasks:admin</c> serves: each one asks.</summary>
    public static TheoryData<string> AdminTools => [.. McpToolScopes.All
        .Where(pair => pair.Value == McpScopes.TasksAdmin)
        .Select(pair => pair.Key)];

    [Theory]
    [MemberData(nameof(AdminTools))]
    public async Task A_confirmed_admin_tool_runs_after_the_prompt(string tool)
    {
        var consent = new ScriptedConsent(UserConsentOutcome.Verified);
        var audit = new RecordingAuditLog();
        await using var harness = new Harness(audit, consent);
        using var client = harness.CreateClient();

        var body = await CallToolAsync(client, tool, [McpScopes.TasksAdmin], new { id = Guid.NewGuid() });

        // An unknown goal: the tool itself answered, so the step-up let the call through.
        Assert.DoesNotContain("Windows Hello", body, StringComparison.Ordinal);
        Assert.Contains("goal_not_found", body, StringComparison.Ordinal);
        var prompt = Assert.Single(consent.Prompts);
        Assert.Contains(TestGrant, prompt, StringComparison.Ordinal);
        Assert.Contains(tool, prompt, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AdminTools))]
    public async Task A_cancelled_prompt_refuses_the_admin_tool(string tool)
    {
        var consent = new ScriptedConsent(UserConsentOutcome.Declined);
        var audit = new RecordingAuditLog();
        await using var harness = new Harness(audit, consent);
        using var client = harness.CreateClient();

        var body = await CallToolAsync(client, tool, [McpScopes.TasksAdmin], new { id = Guid.NewGuid() });

        Assert.Contains("needs Windows Hello on the desktop, and it was cancelled", body, StringComparison.Ordinal);
        Assert.DoesNotContain("goal_not_found", body, StringComparison.Ordinal);
        Assert.Single(consent.Prompts);
        var call = Assert.Single(audit.Records);
        Assert.Equal(McpCallOutcome.Refused, call.Outcome);
        Assert.Equal(TestGrant, call.Caller);
    }

    [Theory]
    [MemberData(nameof(AdminTools))]
    public async Task An_unavailable_prompt_refuses_the_admin_tool(string tool)
    {
        var consent = new ScriptedConsent(UserConsentOutcome.NotAvailable);
        var audit = new RecordingAuditLog();
        await using var harness = new Harness(audit, consent);
        using var client = harness.CreateClient();

        var body = await CallToolAsync(client, tool, [McpScopes.TasksAdmin], new { id = Guid.NewGuid() });

        Assert.Contains("needs Windows Hello on the desktop, and it is not available here", body, StringComparison.Ordinal);
        Assert.DoesNotContain("goal_not_found", body, StringComparison.Ordinal);
        Assert.Equal(McpCallOutcome.Refused, Assert.Single(audit.Records).Outcome);
    }

    [Fact]
    public async Task A_transition_asks_for_nothing()
    {
        var consent = new ScriptedConsent(UserConsentOutcome.Declined);
        await using var harness = new Harness(new RecordingAuditLog(), consent);
        using var client = harness.CreateClient();

        var body = await CallToolAsync(client, "transition", [McpScopes.TasksWrite], new { id = Guid.NewGuid(), status = "done" });

        Assert.DoesNotContain("Windows Hello", body, StringComparison.Ordinal);
        Assert.Empty(consent.Prompts);
    }

    [Fact]
    public async Task The_harness_caller_asks_for_nothing()
    {
        var consent = new ScriptedConsent(UserConsentOutcome.Declined);
        await using var harness = new Harness(new RecordingAuditLog(), consent);
        using var client = harness.CreateClient();

        // No test principal: the harness's own, which keeps no step-up.
        var body = await CallToolAsync(client, "delete_goal", null, new { id = Guid.NewGuid() });

        Assert.DoesNotContain("Windows Hello", body, StringComparison.Ordinal);
        Assert.Contains("goal_not_found", body, StringComparison.Ordinal);
        Assert.Empty(consent.Prompts);
    }

    [Fact]
    public void Only_admin_tools_ask_and_never_for_the_harness()
    {
        var grant = McpScopePolicies.Static(TestGrant, McpScopes.Grantable);

        Assert.True(McpStepUp.Requires("delete_goal", grant));
        Assert.False(McpStepUp.Requires("transition", grant));
        Assert.False(McpStepUp.Requires("delete_goal", McpScopePolicies.HarnessCaller));
        Assert.False(McpStepUp.Requires(null, grant));
    }

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

    private sealed class Harness(IMcpAuditLog audit, IUserConsent consent) : IsolatedHarnessFactory<DesktopHarness::Program>
    {
        protected override void ConfigureHarness(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IAppFeatureSettings>(new EveryFeature());
                services.AddSingleton(audit);
                services.AddSingleton(consent);
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

    /// <summary>Every group listed, so the tools here are reached whatever the harness's defaults.</summary>
    private sealed class EveryFeature : IAppFeatureSettings
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

    /// <summary>A consent that answers as told and keeps every prompt it was shown.</summary>
    private sealed class ScriptedConsent(UserConsentOutcome outcome) : IUserConsent
    {
        private readonly List<string> _prompts = [];

        public IReadOnlyList<string> Prompts
        {
            get
            {
                lock (_prompts) return [.. _prompts];
            }
        }

        public Task<UserConsentOutcome> VerifyAsync(string message, CancellationToken cancellationToken = default)
        {
            lock (_prompts) _prompts.Add(message);
            return Task.FromResult(outcome);
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
