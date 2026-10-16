namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The direction beside the status: the arrow pad where the block may state one and
/// the host can write it, the badge where it cannot, and the bare word a record
/// states where nothing places it — and the record asking its pane's scope.
/// </summary>
public sealed class DevbookSyncSelectorTests
{
    private const string Focus = "Blazor._internal.domWrapper.focus";
    private const string AnchorBelow = "backlogAnchorBelow";

    private static readonly DevbookSyncState Inherited =
        new(DevbookSyncLevel.Unit, null, new DevbookSyncDirection("push", DevbookSyncLevel.Context));

    private static readonly DevbookSyncState StatedPull =
        new(DevbookSyncLevel.Unit, "pull", new DevbookSyncDirection("pull", DevbookSyncLevel.Unit));

    private static readonly DevbookSyncState StatedSync =
        new(DevbookSyncLevel.Unit, "sync", new DevbookSyncDirection("sync", DevbookSyncLevel.Unit));

    private static readonly DevbookSyncState DefaultReport =
        new(DevbookSyncLevel.Page, null, new DevbookSyncDirection("report", null));

    private static IRenderedComponent<DevbookSyncSelector> Pad(BunitContext context, DevbookSyncState state, List<string?>? picked = null)
    {
        context.JSInterop.SetupVoid(Focus, _ => true);
        context.JSInterop.Setup<double[]?>(AnchorBelow, _ => true).SetResult([120.5, 48]);

        return context.Render<DevbookSyncSelector>(parameters => parameters
            .Add(p => p.State, state)
            .Add(p => p.Editable, true)
            .Add(p => p.Label, "Sync direction for Order")
            .Add(p => p.OnChanged, (string? value) => picked?.Add(value)));
    }

    private static string? Pressed(IRenderedComponent<DevbookSyncSelector> pad, string part) =>
        pad.Find($"[data-testid='devbook-sync-{part}']").GetAttribute("aria-pressed");

    [Fact]
    public void Editable_it_is_a_named_pad_of_four_keys_around_the_direction_in_force()
    {
        using var context = new BunitContext();

        var pad = Pad(context, Inherited);

        var group = pad.Find("[data-testid='devbook-sync']");
        Assert.Equal("group", group.GetAttribute("role"));
        Assert.Equal("Sync direction for Order", group.GetAttribute("aria-label"));
        Assert.True(group.HasAttribute("data-arrow-pad"));
        Assert.Empty(pad.FindAll("select"));

        var keys = group.QuerySelectorAll(".sync-pad__key")
            .Select(key => (key.GetAttribute("aria-label"), key.GetAttribute("title"), key.GetAttribute("aria-keyshortcuts")))
            .ToList();
        Assert.Equal(
            [
                ("Report (↑)", "Report (↑)", "ArrowUp"),
                ("Pull (←)", "Pull (←)", "ArrowLeft"),
                ("Push (→)", "Push (→)", "ArrowRight"),
                ("Inherit (↓)", "Inherit (↓)", "ArrowDown")
            ],
            keys);

        var value = pad.Find("[data-testid='devbook-sync-value']");
        Assert.Equal("sync push · from context", value.TextContent);
        Assert.Contains("badge--sync-push", value.GetAttribute("class"), StringComparison.Ordinal);
        Assert.Equal("menu", value.GetAttribute("aria-haspopup"));
    }

    [Fact]
    public void The_centre_says_whether_the_direction_is_stated_inherited_or_the_default()
    {
        using var context = new BunitContext();

        Assert.Equal("sync pull", Pad(context, StatedPull).Find("[data-testid='devbook-sync-value']").TextContent);
        Assert.Equal("sync report · default", Pad(context, DefaultReport).Find("[data-testid='devbook-sync-value']").TextContent);
    }

    /// <summary>The pressed key is what the block states — Down while it states
    /// nothing — never the direction it inherits: pressing that key is how a reader
    /// would state it.</summary>
    [Fact]
    public void The_pressed_key_is_what_the_block_states_and_it_is_the_one_tab_stop()
    {
        using var context = new BunitContext();

        var stated = Pad(context, StatedPull);
        Assert.Equal("true", Pressed(stated, "pull"));
        Assert.Equal("false", Pressed(stated, "push"));
        Assert.Equal("false", Pressed(stated, "inherit"));
        Assert.Equal(["0"], stated.FindAll("[tabindex='0']").Select(part => part.GetAttribute("tabindex")));
        Assert.Equal("0", stated.Find("[data-testid='devbook-sync-pull']").GetAttribute("tabindex"));

        var inheriting = Pad(context, Inherited);
        Assert.Equal("true", Pressed(inheriting, "inherit"));
        Assert.Equal("false", Pressed(inheriting, "push"));
        Assert.Equal("0", inheriting.Find("[data-testid='devbook-sync-inherit']").GetAttribute("tabindex"));

        // sync has no key, so the centre that names it holds the stop.
        var keyless = Pad(context, StatedSync);
        Assert.Empty(keyless.FindAll(".sync-pad__key[aria-pressed='true']"));
        Assert.Equal("0", keyless.Find("[data-testid='devbook-sync-value']").GetAttribute("tabindex"));
    }

    [Theory]
    [InlineData("ArrowRight", "push")]
    [InlineData("ArrowLeft", "pull")]
    [InlineData("ArrowUp", "report")]
    public void An_arrow_states_its_direction_once(string key, string direction)
    {
        using var context = new BunitContext();
        var picked = new List<string?>();

        var pad = Pad(context, Inherited, picked);
        pad.Find("[data-testid='devbook-sync-inherit']").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Equal([direction], picked);
    }

    [Fact]
    public void An_arrow_works_from_the_centre_too()
    {
        using var context = new BunitContext();
        var picked = new List<string?>();

        var pad = Pad(context, StatedSync, picked);
        pad.Find("[data-testid='devbook-sync-value']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        Assert.Equal(["push"], picked);
    }

    [Fact]
    public void Down_clears_what_the_block_states_so_it_inherits()
    {
        using var context = new BunitContext();
        var picked = new List<string?>();

        var pad = Pad(context, StatedPull, picked);
        pad.Find("[data-testid='devbook-sync-pull']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        Assert.Equal([(string?)null], picked);
    }

    [Theory]
    [InlineData("push", "push")]
    [InlineData("pull", "pull")]
    [InlineData("report", "report")]
    [InlineData("inherit", null)]
    public void Clicking_a_key_states_its_direction(string part, string? direction)
    {
        using var context = new BunitContext();
        var picked = new List<string?>();

        var pad = Pad(context, StatedSync, picked);
        pad.Find($"[data-testid='devbook-sync-{part}']").Click();

        Assert.Equal([direction], picked);
    }

    /// <summary>A key that would state what the block already states is no change,
    /// the way a native <c>change</c> does not fire for the value already
    /// selected — re-raising it would rewrite the same line through the host.</summary>
    [Fact]
    public void A_key_naming_what_the_block_already_states_raises_nothing()
    {
        using var context = new BunitContext();
        var picked = new List<string?>();

        Pad(context, StatedPull, picked).Find("[data-testid='devbook-sync-pull']").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        Pad(context, Inherited, picked).Find("[data-testid='devbook-sync-inherit']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Pad(context, StatedPull, picked).Find("[data-testid='devbook-sync-pull']").Click();

        Assert.Empty(picked);
    }

    /// <summary>The host writes a file per change and reads the block back after,
    /// so until it has, every repeat of a held arrow would pass the "already
    /// stated" check and queue the same write again.</summary>
    [Fact]
    public void A_held_arrow_states_once_and_ignores_its_repeats()
    {
        using var context = new BunitContext();
        var picked = new List<string?>();

        var pad = Pad(context, Inherited, picked);
        var key = pad.Find("[data-testid='devbook-sync-inherit']");
        key.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        pad.Find("[data-testid='devbook-sync-push']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight", Repeat = true });
        pad.Find("[data-testid='devbook-sync-push']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight", Repeat = true });

        Assert.Equal(["push"], picked);
    }

    /// <summary>A key held with a modifier belongs to whatever claimed that chord —
    /// Alt+Left is the browser's Back.</summary>
    [Theory]
    [InlineData("ArrowLeft", true, false, false, false)]
    [InlineData("ArrowUp", true, false, false, false)]
    [InlineData("ArrowRight", false, true, false, false)]
    [InlineData("ArrowLeft", false, false, true, false)]
    [InlineData("ArrowUp", false, false, false, true)]
    public void A_key_held_with_a_modifier_is_not_the_pads(string key, bool alt, bool ctrl, bool meta, bool shift)
    {
        using var context = new BunitContext();
        var picked = new List<string?>();

        var pad = Pad(context, Inherited, picked);
        pad.Find("[data-testid='devbook-sync-inherit']").KeyDown(new KeyboardEventArgs
        {
            Key = key,
            AltKey = alt,
            CtrlKey = ctrl,
            MetaKey = meta,
            ShiftKey = shift
        });

        Assert.Empty(picked);
        Assert.Empty(context.JSInterop.Invocations[Focus]);
    }

    [Theory]
    [InlineData("Enter")]
    [InlineData(" ")]
    [InlineData("Tab")]
    [InlineData("Home")]
    [InlineData("p")]
    public void Any_other_key_is_left_alone(string key)
    {
        using var context = new BunitContext();
        var picked = new List<string?>();

        var pad = Pad(context, Inherited, picked);
        pad.Find("[data-testid='devbook-sync-inherit']").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Empty(picked);
        Assert.Empty(context.JSInterop.Invocations[Focus]);
    }

    /// <summary>Roving: the ring goes to the key the arrow reached, which becomes the
    /// tab stop, so Tab leaves the pad and Shift+Tab comes back to where it was.</summary>
    [Fact]
    public void An_arrow_takes_the_focus_and_the_tab_stop_to_its_key()
    {
        using var context = new BunitContext();

        var pad = Pad(context, Inherited);
        pad.Find("[data-testid='devbook-sync-inherit']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        Assert.Single(context.JSInterop.Invocations[Focus]);
        Assert.Equal("0", pad.Find("[data-testid='devbook-sync-push']").GetAttribute("tabindex"));
        Assert.Equal("-1", pad.Find("[data-testid='devbook-sync-inherit']").GetAttribute("tabindex"));
        Assert.Single(pad.FindAll("[tabindex='0']"));
    }

    [Fact]
    public void The_centre_opens_a_menu_for_the_directions_with_no_key()
    {
        using var context = new BunitContext();
        var picked = new List<string?>();

        var pad = Pad(context, Inherited, picked);
        pad.Find("[data-testid='devbook-sync-value']").Click();

        Assert.Equal("true", pad.Find("[data-testid='devbook-sync-value']").GetAttribute("aria-expanded"));
        var items = pad.FindAll("[role='menuitem']").Select(item => item.GetAttribute("data-testid")).ToList();
        Assert.Equal(["devbook-sync-menu-item-sync", "devbook-sync-menu-item-off"], items);

        pad.Find("[data-testid='devbook-sync-menu-item-off']").Click();

        Assert.Equal(["off"], picked);
        Assert.Empty(pad.FindAll("[role='menu']"));
    }

    /// <summary>Enter or Space on the centre is a click with no pointer — detail 0,
    /// at (0,0) — and the centre is the only way to sync and off, so the menu
    /// goes under the centre rather than to the click's position.</summary>
    [Fact]
    public void A_menu_opened_from_the_keyboard_sits_under_the_centre_not_in_the_corner()
    {
        using var context = new BunitContext();

        var pad = Pad(context, Inherited);
        pad.Find("[data-testid='devbook-sync-value']").Click(new MouseEventArgs { Detail = 0, ClientX = 0, ClientY = 0 });

        var style = pad.Find("[data-testid='devbook-sync-menu']").GetAttribute("style");
        Assert.Equal("--context-menu-x: 120.5px; --context-menu-y: 48px", style);
        var anchored = Assert.Single(context.JSInterop.Invocations[AnchorBelow]);
        Assert.IsType<ElementReference>(Assert.Single(anchored.Arguments));
    }

    /// <summary>Where the page cannot say where the centre is, the click's own
    /// position stands in rather than the menu staying shut.</summary>
    [Fact]
    public void A_menu_the_page_cannot_place_opens_at_the_pointer()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid(Focus, _ => true);
        context.JSInterop.Setup<double[]?>(AnchorBelow, _ => true).SetResult(null);

        var pad = context.Render<DevbookSyncSelector>(parameters => parameters
            .Add(p => p.State, Inherited)
            .Add(p => p.Editable, true));
        pad.Find("[data-testid='devbook-sync-value']").Click(new MouseEventArgs { Detail = 1, ClientX = 30, ClientY = 40 });

        Assert.Equal("--context-menu-x: 30px; --context-menu-y: 40px", pad.Find("[data-testid='devbook-sync-menu']").GetAttribute("style"));
    }

    [Fact]
    public void With_a_direction_stated_the_menu_offers_inheriting_again_and_holds_back_the_one_stated()
    {
        using var context = new BunitContext();
        var picked = new List<string?>();

        var pad = Pad(context, StatedSync, picked);
        pad.Find("[data-testid='devbook-sync-value']").Click();

        Assert.True(pad.Find("[data-testid='devbook-sync-menu-item-sync']").HasAttribute("disabled"));

        pad.Find("[data-testid='devbook-sync-menu-item-inherit']").Click();

        Assert.Equal([(string?)null], picked);
    }

    [Fact]
    public void Read_only_it_is_the_badge_with_the_same_words()
    {
        using var context = new BunitContext();

        var badge = context.Render<DevbookSyncSelector>(parameters => parameters.Add(p => p.State, Inherited));

        Assert.Empty(badge.FindAll("button"));
        var span = badge.Find("span");
        Assert.Equal("badge badge--sync badge--sync-push", span.GetAttribute("class"));
        Assert.Equal("sync push · context", span.TextContent);
        Assert.Contains("inherited from the context", span.GetAttribute("title"), StringComparison.Ordinal);
    }

    [Fact]
    public void Placed_nowhere_it_shows_only_what_the_block_states_and_nothing_when_it_states_nothing()
    {
        using var context = new BunitContext();

        var fallback = context.Render<DevbookSyncSelector>(parameters => parameters.Add(p => p.Fallback, "off"));
        Assert.Equal("sync off", fallback.Find("span").TextContent);
        Assert.Empty(fallback.FindAll("[data-arrow-pad]"));

        var nothing = context.Render<DevbookSyncSelector>();
        Assert.Equal(string.Empty, nothing.Markup.Trim());
    }

    [Fact]
    public void A_record_inside_a_scope_asks_it_for_its_own_block_and_writes_through_it()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid(Focus, _ => true);
        var asked = new List<(DevbookMetadataLevel, string?)>();
        var written = new List<(DevbookMetadataLevel, string?, string?)>();
        var scope = new DevbookSyncScope(
            (level, heading) => { asked.Add((level, heading)); return Inherited; },
            (level, heading, direction) => { written.Add((level, heading, direction)); return Task.CompletedTask; });

        var record = context.Render<CascadingValue<DevbookSyncScope>>(parameters => parameters
            .Add(p => p.Value, scope)
            .AddChildContent<MetadataView>(view => view
                .Add(p => p.Metadata, MetadataReader.Parse("type: aggregate"))
                .Add(p => p.Folder, DevbookFolder.Domain)
                .Add(p => p.HeadingText, "Order")));

        Assert.Contains((DevbookMetadataLevel.Chapter, "Order"), asked);

        record.Find("[data-testid='devbook-sync-push']").Click();

        Assert.Equal([(DevbookMetadataLevel.Chapter, (string?)"Order", (string?)"push")], written);
    }

    [Fact]
    public void Outside_a_devbook_folder_a_scope_is_not_asked()
    {
        using var context = new BunitContext();
        var scope = new DevbookSyncScope((_, _) => Inherited);

        var record = context.Render<CascadingValue<DevbookSyncScope>>(parameters => parameters
            .Add(p => p.Value, scope)
            .AddChildContent<MetadataView>(view => view.Add(p => p.Metadata, MetadataReader.Parse("status: draft"))));

        Assert.Empty(record.FindAll("[data-testid='devbook-sync']"));
    }

    /// <summary>bUnit cannot see a default refused, so the refusal is read where it
    /// lives: the pad's arrows, unmodified, on the pad's own buttons, and only the
    /// default.</summary>
    [Fact]
    public void Components_js_refuses_the_pads_arrows_their_scroll_and_nothing_else()
    {
        var listener = ComponentsScript.Region("const ARROW_PAD_SWALLOWED_KEYS", "});");

        Assert.Contains("'ArrowDown', 'ArrowUp', 'ArrowLeft', 'ArrowRight'", listener, StringComparison.Ordinal);
        Assert.Contains("event.altKey || event.ctrlKey || event.metaKey || event.shiftKey", listener, StringComparison.Ordinal);
        Assert.Contains("[data-arrow-pad] > button", listener, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault()", listener, StringComparison.Ordinal);
        Assert.DoesNotContain("stopPropagation", listener, StringComparison.Ordinal);
    }
}
