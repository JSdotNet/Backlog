using Backlog.SharedKernel.Mcp;
using Backlog.UI.Components.Buttons;
using Backlog.UI.Components.Settings;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using static Backlog.Desktop.UI.UnitTests.McpServerSettingsPageTests;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The MCP server page's Agents card (local ADR 0052 §3): one row per grant not revoked, with
/// its role, source, last use and scopes; Edit scopes opening the switches in place, each
/// stored on change; Revoke confirmed in place; and Add agent in two steps - name, role and
/// switches, then the registration to copy. The grants are an in-memory stand-in at
/// <see cref="IMcpGrantStore"/>; the file store behind it is FileMcpGrantStoreTests'.
/// </summary>
public sealed class McpServerAgentsCardTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "No agent can reach Backlog yet.")]
    [InlineData(1, "1 agent can reach Backlog.")]
    [InlineData(3, "3 agents can reach Backlog.")]
    public void The_headline_counts_the_agents_that_can_reach_Backlog(int count, string headline)
    {
        var grants = new FakeGrantStore();
        for (var i = 0; i < count; i++) grants.Create($"agent {i}", ["tasks:read"], McpAgents.CreatedInSettings);
        grants.Revoke(grants.Create("gone", ["tasks:read"], McpAgents.CreatedInSettings).Id);
        using var context = Context(new FakeMcpServer { IsListening = true }, grants: grants);

        var frame = context.Render<SettingsFrame>(parameters => parameters
            .Add(f => f.Title, "MCP server")
            .AddChildContent<McpServerSettingsPage>());

        Assert.Equal(headline, frame.Find("[data-testid='settings-headline']").TextContent.Trim());
    }

    [Fact]
    public void A_listener_that_is_not_listening_keeps_the_headline_about_its_port()
    {
        var grants = new FakeGrantStore();
        grants.Create("Backlog delivery", ["tasks:read"], McpAgents.CreatedInSettings);
        using var context = Context(new FakeMcpServer { Unavailable = Collision }, grants: grants);

        var frame = context.Render<SettingsFrame>(parameters => parameters
            .Add(f => f.Title, "MCP server")
            .AddChildContent<McpServerSettingsPage>());

        Assert.Equal("The MCP server is not listening on port 5757.", frame.Find("[data-testid='settings-headline']").TextContent.Trim());
    }

    [Fact]
    public void A_row_says_the_agent_s_role_source_and_last_use_and_its_scopes_in_switch_order()
    {
        var grants = new FakeGrantStore();
        var delivery = grants.Create("Backlog delivery", ["runs:write", "notify:ask", "devbook:read", "tasks:write", "tasks:read"], McpAgents.CreatedInSettings);
        grants.Create("Hand-made", ["tasks:read", "agents:grant"], "setup");
        var audit = new FakeAuditLog(
            new McpAuditRecord(Now.AddMinutes(-30), $"Backlog delivery ({delivery.Id})", "read_item", "1", McpCallOutcome.Succeeded, "t-1"),
            new McpAuditRecord(Now.AddMinutes(-4), $"Backlog delivery ({delivery.Id})", "comment", "1", McpCallOutcome.Succeeded, "t-2"),
            new McpAuditRecord(Now.AddMinutes(-1), "Hand-made", "comment", "1", McpCallOutcome.Succeeded, "t-3"));
        using var context = Context(new FakeMcpServer { IsListening = true }, audit, grants);
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));

        var page = context.Render<McpServerSettingsPage>();

        Assert.Equal(["Backlog delivery", "Hand-made"], Column(page, "agent-row-name"));
        Assert.Equal(
            ["Role: Delivery flow · added in Settings · last used 4 min ago", "Role: Custom · created by setup · not used yet"],
            Column(page, "agent-row-meta"));

        var rows = page.FindAll("[data-testid='mcp-server-agent']");
        Assert.Equal(
            ["tasks:read", "tasks:write", "devbook:read", "runs:write", "notify:ask"],
            rows[0].QuerySelectorAll("[data-testid='agent-row-scope']").Select(chip => chip.TextContent.Trim()));
        Assert.Contains("badge--tone-alert", rows[1].QuerySelectorAll("[data-testid='agent-row-scope']")[1].ClassName, StringComparison.Ordinal);
        Assert.Equal(delivery.Id, rows[0].GetAttribute("data-grant"));
    }

    [Fact]
    public void Revoke_asks_in_place_and_Keep_leaves_the_agent_alone()
    {
        var grants = new FakeGrantStore();
        grants.Create("Backlog delivery", ["tasks:read"], McpAgents.CreatedInSettings);
        using var context = Context(new FakeMcpServer { IsListening = true }, grants: grants);
        var page = context.Render<McpServerSettingsPage>();

        page.Find("[data-testid='agent-row-revoke']").Click();

        Assert.Equal("Revoke Backlog delivery? Its sessions lose access now.", Text(page, "agent-row-revoke-question"));
        Assert.Empty(page.FindAll("[data-testid='agent-row-revoke']"));
        Assert.Equal(0, grants.Revokes);

        page.Find("[data-testid='agent-row-revoke-keep']").Click();

        Assert.Empty(page.FindAll("[data-testid='agent-row-revoke-question']"));
        Assert.Single(page.FindAll("[data-testid='mcp-server-agent']"));
        Assert.Equal(0, grants.Revokes);
    }

    [Fact]
    public void The_second_Revoke_revokes_the_grant_and_its_row_leaves_the_list()
    {
        var grants = new FakeGrantStore();
        var delivery = grants.Create("Backlog delivery", ["tasks:read"], McpAgents.CreatedInSettings);
        grants.Create("Personal assistant", ["tasks:read"], McpAgents.CreatedInSettings);
        using var context = Context(new FakeMcpServer { IsListening = true }, grants: grants);
        var page = context.Render<McpServerSettingsPage>();

        page.FindAll("[data-testid='agent-row-revoke']")[0].Click();
        page.Find("[data-testid='agent-row-revoke-confirm']").Click();

        Assert.True(grants.Find(delivery.Id)?.Revoked);
        Assert.Equal(["Personal assistant"], Column(page, "agent-row-name"));
    }

    [Fact]
    public void Edit_scopes_opens_the_switches_under_the_row_and_reads_Done_while_open()
    {
        var grants = new FakeGrantStore();
        var delivery = grants.Create("Backlog delivery", ["tasks:read"], McpAgents.CreatedInSettings);
        grants.Create("Personal assistant", ["tasks:read"], McpAgents.CreatedInSettings);
        using var context = Context(new FakeMcpServer { IsListening = true }, grants: grants);
        var page = context.Render<McpServerSettingsPage>();

        Assert.Empty(page.FindAll("[data-testid='mcp-server-agent-scopes']"));

        page.FindAll("[data-testid='agent-row-edit']")[0].Click();

        var edit = page.FindAll("[data-testid='agent-row-edit']")[0];
        Assert.Equal("Done", edit.TextContent.Trim());
        Assert.Equal("true", edit.GetAttribute("aria-expanded"));
        Assert.Equal($"mcp-agent-editor-{delivery.Id}", edit.GetAttribute("aria-controls"));
        Assert.Single(page.FindAll("[data-testid='mcp-server-agent-scopes']"));

        // One open at a time: opening the second closes the first.
        page.FindAll("[data-testid='agent-row-edit']")[1].Click();

        Assert.Equal(["Edit scopes", "Done"], Column(page, "agent-row-edit"));
        Assert.Single(page.FindAll("[data-testid='mcp-server-agent-scopes']"));
    }

    [Fact]
    public void A_switch_stores_its_scope_at_once_and_raises_the_grant_s_version()
    {
        var grants = new FakeGrantStore();
        var delivery = grants.Create("Backlog delivery", ["tasks:read"], McpAgents.CreatedInSettings);
        using var context = Context(new FakeMcpServer { IsListening = true }, grants: grants);
        var page = context.Render<McpServerSettingsPage>();
        page.Find("[data-testid='agent-row-edit']").Click();

        Switch(page, "mcp-server-agent-scopes", "devbook:read").Click();

        Assert.Equal(["tasks:read", "devbook:read"], grants.Find(delivery.Id)?.Scopes);
        Assert.Equal(2, grants.Find(delivery.Id)?.Version);
        Assert.Equal(["tasks:read", "devbook:read"], Column(page, "agent-row-scope"));

        Switch(page, "mcp-server-agent-scopes", "tasks:read").Click();

        Assert.Equal(["devbook:read"], grants.Find(delivery.Id)?.Scopes);
        Assert.DoesNotContain(page.FindAll("button"), button => button.TextContent.Trim() is "Save" or "Apply" or "Cancel");
    }

    [Fact]
    public void The_switches_are_McpScopes_in_its_order_and_every_role_holds_only_grantable_unlocked_scopes()
    {
        Assert.Equal(McpScopes.All, McpAgents.Scopes.Select(option => option.Name));
        Assert.Equal([McpScopes.DevbookDecide, McpScopes.AgentsGrant], McpAgents.Scopes.Where(option => option.Locked).Select(option => option.Name));
        Assert.All(McpAgents.Scopes, option => Assert.False(string.IsNullOrWhiteSpace(option.Description)));
        Assert.All(McpAgents.Roles.SelectMany(role => role.Scopes), scope =>
        {
            Assert.True(McpScopes.IsGrantable(scope), scope);
            Assert.DoesNotContain(scope, McpAgents.Locked);
        });
    }

    [Fact]
    public void A_scope_no_grant_may_hold_is_dropped_on_the_next_change_rather_than_refusing_it()
    {
        var grants = new FakeGrantStore();
        var old = grants.Create("Old hook", ["notify:ask"], "setup");
        grants.Smuggle(old.Id, ["questions", "notify:ask"]);
        using var context = Context(new FakeMcpServer { IsListening = true }, grants: grants);
        var page = context.Render<McpServerSettingsPage>();
        page.Find("[data-testid='agent-row-edit']").Click();

        Switch(page, "mcp-server-agent-scopes", "tasks:read").Click();

        Assert.Equal(["tasks:read", "notify:ask"], grants.Find(old.Id)?.Scopes);
        Assert.Empty(page.FindAll("[data-testid='mcp-server-agents-error']"));
    }

    [Fact]
    public void Devbook_decide_and_agents_grant_are_drawn_locked()
    {
        var grants = new FakeGrantStore();
        grants.Create("Manager session", ["tasks:read", "agents:grant"], "setup");
        using var context = Context(new FakeMcpServer { IsListening = true }, grants: grants);
        var page = context.Render<McpServerSettingsPage>();
        page.Find("[data-testid='agent-row-edit']").Click();

        var decide = Switch(page, "mcp-server-agent-scopes", "devbook:decide");
        var grant = Switch(page, "mcp-server-agent-scopes", "agents:grant");

        Assert.True(decide.HasAttribute("disabled"));
        Assert.Equal("false", decide.GetAttribute("aria-checked"));
        Assert.True(grant.HasAttribute("disabled"));
        Assert.Equal("true", grant.GetAttribute("aria-checked"));
    }

    [Fact]
    public void Add_agent_fills_the_switches_from_the_role_and_a_switch_flipped_by_hand_makes_it_Custom()
    {
        using var context = Context(new FakeMcpServer { IsListening = true }, grants: new FakeGrantStore());
        var page = context.Render<McpServerSettingsPage>();

        page.Find("[data-testid='mcp-agent-add-toggle']").Click();

        Assert.Equal("Close", Text(page, "mcp-agent-add-toggle"));
        Assert.Equal("Coding agent", SelectedRole(page));
        Assert.Equal(McpAgents.PresetOf("Coding agent"), OnSwitches(page, "mcp-agent-new-scopes"));
        Assert.True(Switch(page, "mcp-agent-new-scopes", "devbook:decide").HasAttribute("disabled"));

        RoleSelect(page).Change("Read-only + capture");

        Assert.Equal(["tasks:read", "devbook:read", "inbox:capture"], OnSwitches(page, "mcp-agent-new-scopes"));

        Switch(page, "mcp-agent-new-scopes", "notify:ask").Click();

        Assert.Equal("Custom", SelectedRole(page));
        Assert.Equal(["tasks:read", "devbook:read", "notify:ask", "inbox:capture"], OnSwitches(page, "mcp-agent-new-scopes"));
    }

    [Fact]
    public void Create_agent_makes_the_grant_and_shows_the_registration_to_copy()
    {
        var grants = new FakeGrantStore();
        using var context = Context(new FakeMcpServer { IsListening = true }, grants: grants);
        var page = context.Render<McpServerSettingsPage>();
        page.Find("[data-testid='mcp-agent-add-toggle']").Click();
        RoleSelect(page).Change("Delivery flow");

        page.Find("[data-testid='mcp-agent-name-input']").Input("Copilot CLI · Backlog");
        page.Find("[data-testid='mcp-agent-create']").Click();

        var created = Assert.Single(grants.List());
        Assert.Equal("Copilot CLI · Backlog", created.Name);
        Assert.Equal(["tasks:read", "tasks:write", "devbook:read", "runs:write", "notify:ask"], created.Scopes);
        Assert.Equal(McpAgents.CreatedInSettings, created.CreatedBy);

        Assert.Empty(page.FindAll("[data-testid='mcp-server-agent-add']"));
        Assert.Equal("Register \"Copilot CLI · Backlog\"", Text(page, "agent-registration-title"));

        var endpoint = new Uri("http://127.0.0.1:5757/mcp");
        var claude = page.FindComponents<CopyButton>().Single(button => button.Instance.TestId == "agent-registration-copy-claude").Instance.Text;
        var copilot = page.FindComponents<CopyButton>().Single(button => button.Instance.TestId == "agent-registration-copy-copilot").Instance.Text;
        Assert.Equal(McpAgents.ClaudeCode(endpoint, created.Id, debug: IsDebug), claude);
        Assert.Equal(McpAgents.Copilot(endpoint, created.Id, debug: IsDebug), copilot);
        Assert.Contains($"--grant {created.Id}", claude, StringComparison.Ordinal);
        Assert.Contains($"--grant {created.Id}", Text(page, "agent-registration-snippet"), StringComparison.Ordinal);
        Assert.Equal(["Copilot CLI · Backlog"], Column(page, "agent-row-name"));

        page.Find("[data-testid='agent-registration-done']").Click();

        Assert.Empty(page.FindAll("[data-testid='mcp-server-agent-registration']"));
        Assert.Equal("Add agent", Text(page, "mcp-agent-add-toggle"));
    }

    [Theory]
    [InlineData("   ", "Give the agent a name.")]
    [InlineData("backlog DELIVERY", "An agent is already called backlog DELIVERY.")]
    public void A_blank_or_taken_name_is_refused_beside_the_field_and_nothing_is_made(string name, string refusal)
    {
        var grants = new FakeGrantStore();
        grants.Create("Backlog delivery", ["tasks:read"], McpAgents.CreatedInSettings);
        using var context = Context(new FakeMcpServer { IsListening = true }, grants: grants);
        var page = context.Render<McpServerSettingsPage>();
        page.Find("[data-testid='mcp-agent-add-toggle']").Click();

        page.Find("[data-testid='mcp-agent-name-input']").Input(name);
        page.Find("[data-testid='mcp-agent-create']").Click();

        Assert.Equal(refusal, Text(page, "mcp-agent-name-status"));
        Assert.Contains("setting__input--error", page.Find("[data-testid='mcp-agent-name-input']").ClassName, StringComparison.Ordinal);
        Assert.Single(grants.List());
        Assert.Single(page.FindAll("[data-testid='mcp-server-agent-add']"));
    }

    [Fact]
    public void A_store_that_cannot_be_written_says_so_on_the_card()
    {
        var grants = new FakeGrantStore();
        grants.Create("Backlog delivery", ["tasks:read"], McpAgents.CreatedInSettings);
        using var context = Context(new FakeMcpServer { IsListening = true }, grants: grants);
        var page = context.Render<McpServerSettingsPage>();
        grants.Failure = new IOException("mcp-grants.json does not parse");

        page.Find("[data-testid='agent-row-revoke']").Click();
        page.Find("[data-testid='agent-row-revoke-confirm']").Click();

        Assert.Equal(McpServerSettingsPage.AgentsUnreadable, Text(page, "mcp-server-agents-error"));
        Assert.Single(page.FindAll("[data-testid='mcp-server-agent']"));
    }

    [Fact]
    public void No_agent_yet_says_so_in_a_sentence()
    {
        using var context = Context(new FakeMcpServer { IsListening = true }, grants: new FakeGrantStore());

        var page = context.Render<McpServerSettingsPage>();

        Assert.Contains("No agent yet.", Text(page, "mcp-server-agents-none"), StringComparison.Ordinal);
        Assert.Equal("Add agent", Text(page, "mcp-agent-add-toggle"));
    }

    [Fact]
    public async Task The_card_follows_a_grant_made_on_another_thread()
    {
        var grants = new FakeGrantStore();
        using var context = Context(new FakeMcpServer { IsListening = true }, grants: grants);
        var page = context.Render<McpServerSettingsPage>();

        await Task.Run(() => grants.Create("Question hook", ["notify:ask"], "upgrade"), TestContext.Current.CancellationToken);

        page.WaitForAssertion(() => Assert.Equal(["Question hook"], Column(page, "agent-row-name")));
    }

    [Fact]
    public async Task Leaving_the_page_lets_go_of_the_grant_store_s_event()
    {
        var grants = new FakeGrantStore();
        using var context = Context(new FakeMcpServer { IsListening = true }, grants: grants);
        context.Render<McpServerSettingsPage>();
        Assert.Equal(1, grants.Subscribers);

        await context.DisposeComponentsAsync();

        Assert.Equal(0, grants.Subscribers);
    }

#if DEBUG
    private const bool IsDebug = true;
#else
    private const bool IsDebug = false;
#endif

    private static AngleSharp.Dom.IElement RoleSelect(IRenderedComponent<McpServerSettingsPage> page) =>
        page.Find("[data-testid='mcp-agent-role-select'] select");

    private static string? SelectedRole(IRenderedComponent<McpServerSettingsPage> page) =>
        RoleSelect(page).GetAttribute("value")
        ?? RoleSelect(page).QuerySelectorAll("option").FirstOrDefault(option => option.HasAttribute("selected"))?.GetAttribute("value");

    private static AngleSharp.Dom.IElement Switch(IRenderedComponent<McpServerSettingsPage> page, string group, string scope) =>
        page.Find($"[data-testid='{group}'] [data-scope='{scope}'] button[role='switch']");

    private static string[] OnSwitches(IRenderedComponent<McpServerSettingsPage> page, string group) =>
        [.. page.FindAll($"[data-testid='{group}'] [data-scope]")
            .Where(toggle => toggle.QuerySelector("button[role='switch']")?.GetAttribute("aria-checked") == "true")
            .Select(toggle => toggle.GetAttribute("data-scope")!)];

    /// <summary>The grants in memory, numbered ids, versions raised the way the file store
    /// raises them, and every call throwing <see cref="Failure"/> while it is set.</summary>
    private sealed class FakeGrantStore : IMcpGrantStore
    {
        private readonly List<McpGrant> _grants = [];
        private readonly Lock _gate = new();
        private Action? _changed;

        public Exception? Failure { get; set; }

        public int Revokes { get; private set; }

        public int Subscribers => _changed?.GetInvocationList().Length ?? 0;

        public event Action? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public IReadOnlyList<McpGrant> List()
        {
            Fail();
            lock (_gate) return [.. _grants];
        }

        public McpGrant? Find(string id)
        {
            Fail();
            lock (_gate) return _grants.FirstOrDefault(grant => grant.Id == id);
        }

        public McpGrant Create(string name, IReadOnlyList<string> scopes, string createdBy)
        {
            Fail();
            Grantable(scopes);
            McpGrant grant;
            lock (_gate)
            {
                grant = new McpGrant($"g_{_grants.Count + 1:D16}", name, [.. scopes], 1, createdBy, Now, false);
                _grants.Add(grant);
            }

            _changed?.Invoke();
            return grant;
        }

        public McpGrant? SetScopes(string id, IReadOnlyList<string> scopes)
        {
            Grantable(scopes);
            return Replace(id, grant => grant.Revoked ? null : grant with { Scopes = [.. scopes], Version = grant.Version + 1 });
        }

        /// <summary>The file store's refusal: a scope no grant may hold.</summary>
        private static void Grantable(IReadOnlyList<string> scopes)
        {
            if (scopes.FirstOrDefault(scope => !McpScopes.IsGrantable(scope)) is { } refused)
            {
                throw new ArgumentException($"No grant may hold '{refused}'.", nameof(scopes));
            }
        }

        public McpGrant? Revoke(string id)
        {
            var revoked = Replace(id, grant => grant with { Revoked = true, Version = grant.Version + 1 });
            if (revoked is not null) Revokes++;
            return revoked;
        }

        private McpGrant? Replace(string id, Func<McpGrant, McpGrant?> change)
        {
            Fail();
            McpGrant? changed;
            lock (_gate)
            {
                var index = _grants.FindIndex(grant => grant.Id == id);
                if (index < 0 || change(_grants[index]) is not { } next) return null;

                _grants[index] = changed = next;
            }

            _changed?.Invoke();
            return changed;
        }

        /// <summary>Writes scopes past the refusal, the way a hand-edited file holds them.</summary>
        public void Smuggle(string id, IReadOnlyList<string> scopes)
        {
            lock (_gate)
            {
                var index = _grants.FindIndex(grant => grant.Id == id);
                _grants[index] = _grants[index] with { Scopes = [.. scopes] };
            }
        }

        private void Fail()
        {
            if (Failure is { } failure) throw failure;
        }
    }
}
