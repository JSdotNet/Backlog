using Backlog.Desktop.UI.Mcp;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.Mcp;
using Backlog.SharedKernel.Mcp;
using Backlog.UI.Components.Settings;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using static Backlog.Desktop.UI.UnitTests.McpServerSettingsPageTests;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// An agent's request waiting for the person (local ADR 0052 §5), where the person meets it:
/// the MCP server page's headline and waiting card, the settings menu's badge, and the request
/// window. Over the real file stores and desk, with Windows Hello faked.
/// </summary>
public sealed class McpServerWaitingCardTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-waiting-card-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FileMcpGrantStore _grants;
    private readonly FileMcpAccessRequestStore _requests;
    private readonly Consent _consent = new();
    private readonly McpAccessDesk _desk;

    public McpServerWaitingCardTests()
    {
        _grants = new FileMcpGrantStore(Path.Combine(_root, FileMcpGrantStore.FileName), _clock);
        _requests = new FileMcpAccessRequestStore(Path.Combine(_root, FileMcpAccessRequestStore.FileName), _clock);
        _desk = new McpAccessDesk(_grants, _requests, _consent, _clock);
    }

    private CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_headline_counts_the_waiting_request_and_the_pending_grant_is_not_an_agent()
    {
        _grants.Create("Backlog delivery", ["tasks:read"], McpAgents.CreatedInSettings);
        await _desk.RegisterAsync("Copilot CLI", ["tasks:read"], "copilot.exe", @"D:\Repos\Backlog", Cancel);
        using var context = PageContext();

        var frame = context.Render<SettingsFrame>(parameters => parameters
            .Add(f => f.Title, "MCP server")
            .AddChildContent<McpServerSettingsPage>());

        Assert.Equal("1 agent can reach Backlog, and 1 is waiting for you.", frame.Find("[data-testid='settings-headline']").TextContent.Trim());
        Assert.Equal(["Backlog delivery"], frame.FindAll("[data-testid='agent-row-name']").Select(name => name.TextContent.Trim()));
        Assert.Equal("Copilot CLI", frame.Find("[data-testid='mcp-server-request'] [data-testid='agent-request-name']").TextContent.Trim());
        Assert.Equal(
            @"Registered itself just now · from copilot.exe · in D:\Repos\Backlog",
            frame.Find("[data-testid='agent-request-meta']").TextContent.Trim());
    }

    [Theory]
    [InlineData(0, 2, "No agent can reach Backlog yet, and 2 are waiting for you.")]
    [InlineData(3, 0, "3 agents can reach Backlog.")]
    public void The_headline_says_how_many_wait(int agents, int waiting, string headline) =>
        Assert.Equal(headline, McpAgentRequests.Headline(agents, waiting));

    [Fact]
    public async Task No_card_is_drawn_while_nothing_waits()
    {
        var registered = await _desk.RegisterAsync("Copilot CLI", ["tasks:read"], null, null, Cancel);
        await _desk.DeclineAsync(registered.Request!.Id, Cancel);
        using var context = PageContext();

        var page = context.Render<McpServerSettingsPage>();

        Assert.Empty(page.FindAll("[data-testid='mcp-server-waiting-card']"));
    }

    [Fact]
    public async Task Trim_scopes_then_Allow_with_Hello_grants_the_trimmed_scopes()
    {
        var registered = await _desk.RegisterAsync("Copilot CLI", ["tasks:read", "tasks:write"], null, null, Cancel);
        using var context = PageContext();
        var page = context.Render<McpServerSettingsPage>();

        page.Find("[data-testid='agent-request-trim']").Click();
        page.Find("[data-testid='agent-request-switches'] [data-scope='tasks:write'] button[role='switch']").Click();
        page.Find("[data-testid='agent-request-allow']").Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid='mcp-server-waiting-card']")));
        Assert.Equal(["tasks:read"], _grants.Find(registered.Grant!.Id)!.Scopes);
        Assert.Equal(["Copilot CLI"], page.FindAll("[data-testid='agent-row-name']").Select(name => name.TextContent.Trim()));
    }

    [Fact]
    public async Task Allow_where_Hello_is_not_available_says_why_and_leaves_the_request_waiting()
    {
        var registered = await _desk.RegisterAsync("Copilot CLI", ["tasks:read"], null, null, Cancel);
        _consent.Answer = UserConsentOutcome.NotAvailable;
        using var context = PageContext();
        var page = context.Render<McpServerSettingsPage>();

        page.Find("[data-testid='agent-request-allow']").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "Windows Hello is not available",
            page.Find("[data-testid='agent-request-status']").TextContent,
            StringComparison.Ordinal));
        Assert.True(_requests.Find(registered.Request!.Id)!.IsOpen);
    }

    [Fact]
    public async Task Decline_on_the_page_revokes_the_pending_grant()
    {
        var registered = await _desk.RegisterAsync("Copilot CLI", ["tasks:read"], null, null, Cancel);
        using var context = PageContext();
        var page = context.Render<McpServerSettingsPage>();

        page.Find("[data-testid='agent-request-decline']").Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid='mcp-server-waiting-card']")));
        Assert.True(_grants.Find(registered.Grant!.Id)!.Revoked);
        Assert.Empty(page.FindAll("[data-testid='agent-row-name']"));
    }

    [Fact]
    public async Task The_request_window_starts_agents_grant_off_and_answers_with_Hello()
    {
        var registered = await _desk.RegisterAsync("Manager session", ["tasks:read", "agents:grant"], "claude.exe", null, Cancel);
        using var context = PageContext();

        var window = context.Render<AgentRequestSurface>(parameters => parameters
            .Add(surface => surface.Query, new Dictionary<string, string> { [AgentRequestSurface.RequestKey] = registered.Request!.Id }));

        Assert.Equal("Manager session wants access", window.Find("#agent-request-window-title").TextContent.Trim());
        Assert.Equal("true", window.Find("[data-scope='tasks:read'] button[role='switch']").GetAttribute("aria-checked"));
        Assert.Equal("false", window.Find("[data-scope='agents:grant'] button[role='switch']").GetAttribute("aria-checked"));
        Assert.Empty(window.FindAll("[data-testid='agent-request-trim']"));
        Assert.Equal(2, window.FindAll(".agent-row__actions button").Count);

        window.Find("[data-testid='agent-request-allow']").Click();

        window.WaitForAssertion(() => Assert.NotEmpty(window.FindAll("[data-testid='agent-request-window-answered']")));
        Assert.Equal(1, _consent.Asked);
        Assert.Equal(["tasks:read"], _grants.Find(registered.Grant!.Id)!.Scopes);
    }

    [Fact]
    public void A_host_with_no_desk_says_so_in_the_window()
    {
        using var context = new BunitContext();

        var window = context.Render<AgentRequestSurface>(parameters => parameters
            .Add(surface => surface.Query, new Dictionary<string, string> { [AgentRequestSurface.RequestKey] = "r_aaaaaaaaaaaaaaaa" }));

        Assert.NotEmpty(window.FindAll("[data-testid='agent-request-window-none']"));
    }

    [Theory]
    [InlineData(true, false, 0, "Listening")]
    [InlineData(true, false, 1, "Listening · 1 waiting")]
    [InlineData(false, true, 2, "Not listening · 2 waiting")]
    public void The_menu_badge_says_how_many_wait_beside_the_listener(bool listening, bool failed, int waiting, string badge) =>
        Assert.Equal(badge, SettingsBadges.McpServer(listening, failed, waiting));

    [Fact]
    public void A_host_with_no_listener_draws_no_badge_whatever_waits() =>
        Assert.Null(SettingsBadges.McpServer(null, false, 3));

    private BunitContext PageContext()
    {
        var context = Context(new FakeMcpServer { IsListening = true }, grants: _grants);
        context.Services.AddSingleton(_desk);
        context.Services.AddSingleton<TimeProvider>(_clock);
        return context;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class Consent : IUserConsent
    {
        public UserConsentOutcome Answer { get; set; } = UserConsentOutcome.Verified;

        public int Asked { get; private set; }

        public Task<UserConsentOutcome> VerifyAsync(string message, CancellationToken cancellationToken = default)
        {
            Asked++;
            return Task.FromResult(Answer);
        }
    }
}
