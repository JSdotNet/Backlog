using Backlog.UI.Components.Buttons;
using Backlog.UI.Components.Settings;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

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

    /// <summary>A row that holds no token - the Unauthenticated row of local ADR 0059 - has
    /// nothing to revoke: no Revoke, while Edit scopes stays.</summary>
    [Fact]
    public void A_row_that_cannot_be_revoked_draws_no_Revoke_and_keeps_Edit_scopes()
    {
        using var context = new BunitContext();

        var row = context.Render<AgentGrantRow>(parameters => parameters
            .Add(r => r.Name, "Unauthenticated")
            .Add(r => r.CanRevoke, false));

        Assert.Empty(row.FindAll("[data-testid='agent-row-revoke']"));
        Assert.Single(row.FindAll("[data-testid='agent-row-edit']"));
    }

    /// <summary>A paired device on the Devices page is the same row with its scopes off: no
    /// chips, its own question, and Revoke buttons named apart from a twin of the same name.</summary>
    [Fact]
    public void A_row_without_scopes_asks_its_hosts_question_and_names_its_buttons_apart()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var row = context.Render<AgentGrantRow>(parameters => parameters
            .Add(r => r.Name, "Pixel 8")
            .Add(r => r.AccessibleName, "Pixel 8, added 2 Oct 2026")
            .Add(r => r.ShowScopes, false)
            .Add(r => r.CanEditScopes, false)
            .Add(r => r.Question, "Revoke Pixel 8? It can no longer sync your backlog.")
            .AddUnmatched("data-device", "33333333"));

        Assert.Empty(row.FindAll("[data-testid='agent-row-scopes']"));
        Assert.Empty(row.FindAll("[data-testid='agent-row-edit']"));
        Assert.Equal("33333333", row.Find("li").GetAttribute("data-device"));
        Assert.Equal("Revoke Pixel 8, added 2 Oct 2026", row.Find("[data-testid='agent-row-revoke']").GetAttribute("aria-label"));

        row.Find("[data-testid='agent-row-revoke']").Click();

        Assert.Equal("Revoke Pixel 8? It can no longer sync your backlog.", row.Find("[data-testid='agent-row-revoke-question']").TextContent.Trim());
        Assert.Equal("Revoke Pixel 8, added 2 Oct 2026 now", row.Find("[data-testid='agent-row-revoke-confirm']").GetAttribute("aria-label"));
    }

    /// <summary>The question replaces the button the focus was on, so the focus goes to the
    /// confirming Revoke; Keep, and a second Revoke that left the row in place, bring it back
    /// to the row's Revoke.</summary>
    [Fact]
    public void The_focus_moves_to_the_confirming_Revoke_and_back_on_Keep_or_a_failed_revoke()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var row = context.Render<AgentGrantRow>(parameters => parameters
            .Add(r => r.Name, "Backlog delivery")
            .Add(r => r.OnRevoke, EventCallback.Factory.Create(this, () => { })));

        row.Find("[data-testid='agent-row-revoke']").Click();
        Assert.Equal(Reference(row, "agent-row-revoke-confirm"), LastFocused(context));

        row.Find("[data-testid='agent-row-revoke-keep']").Click();
        Assert.Equal(Reference(row, "agent-row-revoke"), LastFocused(context));

        row.Find("[data-testid='agent-row-revoke']").Click();
        row.Find("[data-testid='agent-row-revoke-confirm']").Click();
        Assert.Equal(Reference(row, "agent-row-revoke"), LastFocused(context));
    }

    /// <summary>A host whose revocation threw leaves the question standing on an unbound row
    /// otherwise, with nothing to answer it: the row puts its own Revoke back.</summary>
    [Fact]
    public void An_unbound_row_whose_host_threw_on_Revoke_offers_its_Revoke_again()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var attempts = 0;

        var row = context.Render<AgentGrantRow>(parameters => parameters
            .Add(r => r.Name, "Backlog delivery")
            .Add(r => r.OnRevoke, EventCallback.Factory.Create(this, () =>
            {
                attempts++;
                throw new InvalidOperationException("The grant store could not be written.");
            })));

        row.Find("[data-testid='agent-row-revoke']").Click();

        // The row does not hide the host's failure; it only stops asking a question nothing
        // can answer. Nothing is drawn after a throw, so the row is drawn again to read it.
        var confirm = row.Find("[data-testid='agent-row-revoke-confirm']");
        Assert.Throws<InvalidOperationException>(() => confirm.Click());
        Assert.Equal(1, attempts);
        row.Render();

        Assert.Empty(row.FindAll("[data-testid='agent-row-revoke-question']"));
        Assert.Single(row.FindAll("[data-testid='agent-row-revoke']"));
    }

    /// <summary>While the host carries out another row's revocation, this row's Revoke is
    /// drawn but cannot be pressed: one revocation at a time.</summary>
    [Fact]
    public void Revoke_cannot_be_pressed_while_the_host_says_another_revocation_runs()
    {
        using var context = new BunitContext();

        var row = context.Render<AgentGrantRow>(parameters => parameters
            .Add(r => r.Name, "Pixel 8")
            .Add(r => r.RevokeDisabled, true));

        Assert.True(row.Find("[data-testid='agent-row-revoke']").HasAttribute("disabled"));

        row.Render(parameters => parameters.Add(r => r.RevokeDisabled, false));

        Assert.False(row.Find("[data-testid='agent-row-revoke']").HasAttribute("disabled"));
    }

    /// <summary>A circuit that went while the focus was moving has no page to move it on:
    /// the row says nothing, rather than failing the render.</summary>
    [Fact]
    public void A_focus_the_circuit_lost_is_not_an_error()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid(invocation => invocation.Identifier.EndsWith("focus", StringComparison.Ordinal))
            .SetException(new JSDisconnectedException("The circuit went."));

        var row = context.Render<AgentGrantRow>(parameters => parameters
            .Add(r => r.Name, "Backlog delivery"));

        row.Find("[data-testid='agent-row-revoke']").Click();

        // The renderer keeps an exception from the render after the click and throws it at
        // the next render, so drawing the row again is what would fail.
        row.Render();

        Assert.Single(row.FindAll("[data-testid='agent-row-revoke-question']"));
        Assert.Single(context.JSInterop.Invocations, invocation => invocation.Identifier.EndsWith("focus", StringComparison.Ordinal));
    }

    /// <summary>A failed revoke says why in the row, as an alert beside its Revoke; asking
    /// again hides it while the question stands.</summary>
    [Fact]
    public void A_revoke_error_is_drawn_in_the_row_and_gives_way_to_the_question()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var row = context.Render<AgentGrantRow>(parameters => parameters
            .Add(r => r.Name, "Pixel 8")
            .Add(r => r.RevokeError, "The device was not revoked: Windows Hello was cancelled."));

        var error = row.Find("[data-testid='agent-row-revoke-error']");
        Assert.Equal("The device was not revoked: Windows Hello was cancelled.", error.TextContent.Trim());
        Assert.Equal("alert", error.GetAttribute("role"));
        Assert.Contains("agent-row__error", error.ClassName, StringComparison.Ordinal);

        row.Find("[data-testid='agent-row-revoke']").Click();

        Assert.Empty(row.FindAll("[data-testid='agent-row-revoke-error']"));
    }

    /// <summary>Bound, the question is the host's: the row raises the change and draws what
    /// the host answers, so a page can let one row ask at a time.</summary>
    [Fact]
    public void A_bound_row_asks_only_when_its_host_says_so()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var asked = new List<bool>();

        var row = context.Render<AgentGrantRow>(parameters => parameters
            .Add(r => r.Name, "Pixel 8")
            .Add(r => r.Confirming, false)
            .Add(r => r.ConfirmingChanged, EventCallback.Factory.Create<bool>(this, asked.Add)));

        row.Find("[data-testid='agent-row-revoke']").Click();

        Assert.Equal([true], asked);
        Assert.Empty(row.FindAll("[data-testid='agent-row-revoke-question']"));

        row.Render(parameters => parameters.Add(r => r.Confirming, true));

        Assert.Single(row.FindAll("[data-testid='agent-row-revoke-question']"));
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

    /// <summary>The element reference bUnit wrote on one of the row's buttons.</summary>
    private static string? Reference(IRenderedComponent<AgentGrantRow> row, string testId) =>
        row.Find($"[data-testid='{testId}']").GetAttribute("blazor:elementreference");

    /// <summary>The element the last FocusAsync was aimed at.</summary>
    private static string? LastFocused(BunitContext context) =>
        context.JSInterop.Invocations.Last(invocation => invocation.Identifier.EndsWith("focus", StringComparison.Ordinal))
            .Arguments is [ElementReference reference, ..] ? reference.Id : null;
}
