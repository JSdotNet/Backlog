using Backlog.UI.Components.Buttons;
using Backlog.UI.Components.Settings;

using Bunit;

using Microsoft.AspNetCore.Components;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The parts of the MCP server page's Agents card: an agent's row with Revoke confirmed in
/// place and Edit scopes opening its editor under it, the scope switches with a locked
/// scope, and the registration step's two copies. What scopes exist, and what a change
/// stores, is the host's; these pin what each part draws and raises.
/// </summary>
public sealed class AgentComponentsTests
{
    [Fact]
    public void A_row_draws_its_name_line_and_scopes_and_marks_the_highlighted_ones()
    {
        using var context = new BunitContext();

        var row = context.Render<AgentGrantRow>(parameters => parameters
            .Add(r => r.Name, "Manager session")
            .Add(r => r.Meta, "Role: Manager · added in Settings · not used yet")
            .Add(r => r.Scopes, (IReadOnlyList<string>)["tasks:read", "agents:grant"])
            .Add(r => r.Highlighted, (IReadOnlyCollection<string>)["agents:grant"]));

        Assert.Equal("Manager session", row.Find("[data-testid='agent-row-name']").TextContent.Trim());
        Assert.Equal("Role: Manager · added in Settings · not used yet", row.Find("[data-testid='agent-row-meta']").TextContent.Trim());
        var chips = row.FindAll("[data-testid='agent-row-scope']");
        Assert.Equal(["tasks:read", "agents:grant"], chips.Select(chip => chip.TextContent.Trim()));
        Assert.Contains("badge--tone-quiet", chips[0].ClassName, StringComparison.Ordinal);
        Assert.Contains("badge--tone-alert", chips[1].ClassName, StringComparison.Ordinal);
        Assert.Equal("Scopes of Manager session", row.Find("[data-testid='agent-row-scopes']").GetAttribute("aria-label"));
    }

    [Fact]
    public void Revoke_asks_first_and_only_the_second_Revoke_reaches_the_host()
    {
        using var context = new BunitContext();
        var revoked = 0;

        var row = context.Render<AgentGrantRow>(parameters => parameters
            .Add(r => r.Name, "Backlog delivery")
            .Add(r => r.OnRevoke, EventCallback.Factory.Create(this, () => revoked++)));

        row.Find("[data-testid='agent-row-revoke']").Click();

        Assert.Equal(0, revoked);
        var question = row.Find("[data-testid='agent-row-revoke-question']");
        Assert.Equal(AgentGrantRow.RevokeQuestion("Backlog delivery"), question.TextContent.Trim());
        Assert.Equal("Revoke Backlog delivery? Its sessions lose access now.", question.TextContent.Trim());
        Assert.Equal("alert", question.GetAttribute("role"));
        Assert.Empty(row.FindAll("[data-testid='agent-row-edit']"));

        row.Find("[data-testid='agent-row-revoke-confirm']").Click();

        Assert.Equal(1, revoked);
        Assert.Empty(row.FindAll("[data-testid='agent-row-revoke-question']"));
    }

    [Fact]
    public void Edit_scopes_names_the_editor_it_opens_and_draws_it_under_the_row_only_while_open()
    {
        using var context = new BunitContext();
        var toggled = 0;

        IRenderedComponent<AgentGrantRow> Render(bool expanded) => context.Render<AgentGrantRow>(parameters => parameters
            .Add(r => r.Name, "Backlog delivery")
            .Add(r => r.Expanded, expanded)
            .Add(r => r.EditorId, "editor-1")
            .Add(r => r.OnEditScopes, EventCallback.Factory.Create(this, () => toggled++))
            .AddChildContent("<p data-testid='editor-body'>switches</p>"));

        var closed = Render(expanded: false);
        Assert.Equal("Edit scopes", closed.Find("[data-testid='agent-row-edit']").TextContent.Trim());
        Assert.Equal("false", closed.Find("[data-testid='agent-row-edit']").GetAttribute("aria-expanded"));
        Assert.Null(closed.Find("[data-testid='agent-row-edit']").GetAttribute("aria-controls"));
        Assert.Empty(closed.FindAll("[data-testid='editor-body']"));

        closed.Find("[data-testid='agent-row-edit']").Click();
        Assert.Equal(1, toggled);

        var open = Render(expanded: true);
        Assert.Equal("Done", open.Find("[data-testid='agent-row-edit']").TextContent.Trim());
        Assert.Equal("editor-1", open.Find("[data-testid='agent-row-edit']").GetAttribute("aria-controls"));
        Assert.Equal("editor-1", open.Find("[data-testid='agent-row-editor']").Id);
        Assert.Single(open.FindAll("[data-testid='editor-body']"));
    }

    [Fact]
    public void A_switch_raises_its_scope_and_state_and_a_locked_one_is_disabled_as_the_grant_holds_it()
    {
        using var context = new BunitContext();
        var changes = new List<AgentScopeChange>();

        var switches = context.Render<AgentScopeSwitches>(parameters => parameters
            .Add(s => s.Options, (IReadOnlyList<AgentScopeOption>)
            [
                new("tasks:read", "Find and read items"),
                new("tasks:write", "Create, comment"),
                new("devbook:decide", "Never for an agent", Locked: true)
            ])
            .Add(s => s.Selected, (IReadOnlyCollection<string>)["tasks:read"])
            .Add(s => s.AriaLabel, "Scopes of Backlog delivery")
            .Add(s => s.OnChanged, EventCallback.Factory.Create<AgentScopeChange>(this, changes.Add)));

        Assert.Equal("group", switches.Find(".agent-scopes").GetAttribute("role"));
        Assert.Equal("Scopes of Backlog delivery", switches.Find(".agent-scopes").GetAttribute("aria-label"));

        var read = switches.Find("[data-scope='tasks:read'] button[role='switch']");
        var write = switches.Find("[data-scope='tasks:write'] button[role='switch']");
        var decide = switches.Find("[data-scope='devbook:decide'] button[role='switch']");
        Assert.Equal("true", read.GetAttribute("aria-checked"));
        Assert.Equal("false", write.GetAttribute("aria-checked"));
        Assert.True(decide.HasAttribute("disabled"));
        Assert.Contains("agent-scopes__switch--locked", switches.Find("[data-scope='devbook:decide']").ClassName, StringComparison.Ordinal);
        Assert.Contains("Never for an agent", switches.Find("[data-scope='devbook:decide']").TextContent, StringComparison.Ordinal);

        write.Click();
        switches.Find("[data-scope='tasks:read'] button[role='switch']").Click();

        Assert.Equal([new AgentScopeChange("tasks:write", true), new AgentScopeChange("tasks:read", false)], changes);
    }

    [Fact]
    public void The_registration_draws_the_snippet_copies_each_client_s_entry_and_Done_closes()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var done = 0;

        var registration = context.Render<AgentRegistration>(parameters => parameters
            .Add(r => r.Name, "Copilot CLI · Backlog")
            .Add(r => r.Snippet, "\"backlog\": { \"headersHelper\": \"node h.mjs --grant g_1\" }")
            .Add(r => r.ClaudeCode, "claude entry")
            .Add(r => r.Copilot, "copilot entry")
            .Add(r => r.OnDone, EventCallback.Factory.Create(this, () => done++)));

        Assert.Equal("Register \"Copilot CLI · Backlog\"", registration.Find("[data-testid='agent-registration-title']").TextContent.Trim());
        Assert.Contains("--grant g_1", registration.Find("[data-testid='agent-registration-snippet']").TextContent, StringComparison.Ordinal);

        var copies = registration.FindComponents<CopyButton>().ToDictionary(button => button.Instance.TestId!, button => button.Instance);
        Assert.Equal("claude entry", copies["agent-registration-copy-claude"].Text);
        Assert.Equal("Copy for Claude Code", copies["agent-registration-copy-claude"].Label);
        Assert.Equal("copilot entry", copies["agent-registration-copy-copilot"].Text);
        Assert.Equal("Copy for Copilot", copies["agent-registration-copy-copilot"].Label);

        registration.Find("[data-testid='agent-registration-done']").Click();

        Assert.Equal(1, done);
    }
}
