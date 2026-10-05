namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// One outside tool on the settings screen. The card holds nothing but what a
/// field shows: every value is the host's and every act a callback, so these
/// render it with values and assert on the markup and on what it hands back.
/// </summary>
public sealed class ConnectorCardTests
{
    [Fact]
    public void The_head_draws_the_name_and_a_mark_coloured_by_its_token()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectorCard>(p => p
            .Add(c => c.Name, "Tracker")
            .Add(c => c.Icon, "◆")
            .Add(c => c.ColorToken, "--color-primary"));

        Assert.Equal("Tracker", card.Find(".connector-card__name").TextContent);
        var mark = card.Find(".connector-card__mark");
        Assert.Equal("◆", mark.TextContent);
        Assert.Equal("color: var(--color-primary)", mark.GetAttribute("style"));
        Assert.Equal("true", mark.GetAttribute("aria-hidden"));
    }

    [Fact]
    public void A_colour_token_that_is_not_a_token_name_is_ignored()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectorCard>(p => p
            .Add(c => c.Name, "Tracker")
            .Add(c => c.Icon, "◆")
            .Add(c => c.ColorToken, "red; background: url(x)"));

        Assert.Null(card.Find(".connector-card__mark").GetAttribute("style"));
    }

    [Fact]
    public void Without_a_sign_in_there_is_no_account_line_and_no_button()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectorCard>(p => p.Add(c => c.Name, "Issues"));

        Assert.Empty(card.FindAll("[data-testid='connector-account']"));
        Assert.Empty(card.FindAll("[data-testid='connector-sign-in']"));
        Assert.Empty(card.FindAll("[data-testid='connector-sign-out']"));
    }

    [Fact]
    public void Signed_out_offers_sign_in_and_hands_the_press_back()
    {
        using var context = new BunitContext();
        var pressed = 0;

        var card = context.Render<ConnectorCard>(p => p
            .Add(c => c.Name, "Tracker")
            .Add(c => c.SignsIn, true)
            .Add(c => c.OnSignIn, () => pressed++));

        Assert.Equal("Not signed in", card.Find("[data-testid='connector-account']").TextContent.Trim());
        card.Find("[data-testid='connector-sign-in']").Click();

        Assert.Equal(1, pressed);
    }

    [Fact]
    public void A_sign_in_under_way_is_busy_and_cannot_be_pressed_again()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectorCard>(p => p
            .Add(c => c.Name, "Tracker")
            .Add(c => c.SignsIn, true)
            .Add(c => c.SigningIn, true));

        var button = card.Find("[data-testid='connector-sign-in']");
        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal("true", button.GetAttribute("aria-busy"));
    }

    [Fact]
    public void Signed_in_names_the_account_and_offers_sign_out()
    {
        using var context = new BunitContext();
        var pressed = 0;

        var card = context.Render<ConnectorCard>(p => p
            .Add(c => c.Name, "Tracker")
            .Add(c => c.SignsIn, true)
            .Add(c => c.AccountName, "Sam")
            .Add(c => c.OnSignOut, () => pressed++));

        Assert.Equal("Signed in as Sam", card.Find("[data-testid='connector-account']").TextContent.Trim());
        Assert.Empty(card.FindAll("[data-testid='connector-sign-in']"));
        card.Find("[data-testid='connector-sign-out']").Click();

        Assert.Equal(1, pressed);
    }

    [Fact]
    public void A_failed_sign_in_is_shown_as_an_alert()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectorCard>(p => p
            .Add(c => c.Name, "Tracker")
            .Add(c => c.SignsIn, true)
            .Add(c => c.SignInError, "The window was closed."));

        var error = card.Find("[data-testid='connector-sign-in-error']");
        Assert.Equal("alert", error.GetAttribute("role"));
        Assert.Equal("The window was closed.", error.TextContent);
    }

    [Fact]
    public void Without_targets_from_the_host_there_is_no_list_and_no_add_field()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectorCard>(p => p.Add(c => c.Name, "Tracker"));

        Assert.Empty(card.FindAll("[data-testid='connector-no-targets']"));
        Assert.Empty(card.FindAll("[data-testid='connector-add']"));
    }

    [Fact]
    public void An_empty_target_list_says_nothing_is_connected()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectorCard>(p => p
            .Add(c => c.Name, "Tracker")
            .Add(c => c.Targets, []));

        Assert.Equal("Nothing connected yet.", card.Find("[data-testid='connector-no-targets']").TextContent);
        Assert.NotNull(card.Find("[data-testid='connector-add'] input"));
    }

    [Fact]
    public void Each_target_is_a_row_with_its_switch_its_last_sync_and_remove()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectorCard>(p => p
            .Add(c => c.Name, "Tracker")
            .Add(c => c.Targets, [new ConnectorTargetRow("planning", true, "5 Oct, 09:12"), new ConnectorTargetRow("platform", false)]));

        var rows = card.FindAll("[data-testid='connector-target']");
        Assert.Equal(["planning", "platform"], rows.Select(row => row.GetAttribute("data-target")));
        Assert.Equal("true", rows[0].QuerySelector("[role='switch']")!.GetAttribute("aria-checked"));
        Assert.Equal("false", rows[1].QuerySelector("[role='switch']")!.GetAttribute("aria-checked"));
        Assert.Equal("Synced 5 Oct, 09:12", rows[0].QuerySelector("[data-testid='connector-target-synced']")!.TextContent);
        Assert.Equal("Not synced yet", rows[1].QuerySelector("[data-testid='connector-target-synced']")!.TextContent);
        Assert.Equal("Remove planning", rows[0].QuerySelector("[data-testid='connector-target-remove']")!.GetAttribute("aria-label"));
    }

    [Fact]
    public void Flipping_a_switch_hands_back_the_row_with_the_new_value()
    {
        using var context = new BunitContext();
        ConnectorTargetRow? changed = null;

        var card = context.Render<ConnectorCard>(p => p
            .Add(c => c.Name, "Tracker")
            .Add(c => c.Targets, [new ConnectorTargetRow("planning", true)])
            .Add(c => c.OnTargetEnabledChanged, row => changed = row));

        card.Find("[data-testid='connector-target'] [role='switch']").Click();

        Assert.Equal(new ConnectorTargetRow("planning", false), changed);
    }

    [Fact]
    public void Remove_hands_back_the_row()
    {
        using var context = new BunitContext();
        ConnectorTargetRow? removed = null;

        var card = context.Render<ConnectorCard>(p => p
            .Add(c => c.Name, "Tracker")
            .Add(c => c.Targets, [new ConnectorTargetRow("planning", true)])
            .Add(c => c.OnRemoveTarget, row => removed = row));

        card.Find("[data-testid='connector-target-remove']").Click();

        Assert.Equal("planning", removed?.Name);
    }

    [Fact]
    public void Typing_hands_the_draft_back_and_submitting_asks_the_host_to_add()
    {
        using var context = new BunitContext();
        string? draft = null;
        var added = 0;

        var card = context.Render<ConnectorCard>(p => p
            .Add(c => c.Name, "Tracker")
            .Add(c => c.Targets, [])
            .Add(c => c.AddDraftChanged, value => draft = value)
            .Add(c => c.OnAddTarget, () => added++));

        card.Find("[data-testid='connector-add'] input").Input("research");
        card.Find("form.connector-card__add").Submit();

        Assert.Equal("research", draft);
        Assert.Equal(1, added);
    }

    [Fact]
    public void A_refused_add_is_shown_under_the_field()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectorCard>(p => p
            .Add(c => c.Name, "Tracker")
            .Add(c => c.Targets, [])
            .Add(c => c.AddError, "planning is already connected."));

        Assert.Equal("planning is already connected.", card.Find("[data-testid='connector-add'] .field__error").TextContent);
        Assert.Equal("true", card.Find("[data-testid='connector-add'] input").GetAttribute("aria-invalid"));
    }
}
