namespace Backlog.UI.Components.UnitTests;

public sealed class TabsTests
{
    [Fact]
    public void Only_the_active_panel_renders_its_body()
    {
        using var context = new BunitContext();

        var tabs = context.Render<TabsHarness>();

        Assert.Contains("First body", tabs.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Second body", tabs.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Third body", tabs.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_strip_is_a_tablist_of_tabs_pointing_at_their_panels()
    {
        using var context = new BunitContext();

        var tabs = context.Render<TabsHarness>();
        var strip = tabs.Find("[role='tablist']");
        var first = tabs.FindAll("[role='tab']")[0];

        Assert.Equal(3, tabs.FindAll("[role='tab']").Count);
        Assert.Equal(3, tabs.FindAll("[role='tabpanel']").Count);
        Assert.Equal("Sections", strip.GetAttribute("aria-label"));
        Assert.Equal("true", first.GetAttribute("aria-selected"));
        Assert.Equal(tabs.Find("[role='tabpanel']").Id, first.GetAttribute("aria-controls"));
        Assert.Equal(first.Id, tabs.Find("[role='tabpanel']").GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void Exactly_one_tab_is_reachable_with_Tab()
    {
        // Roving tabindex: a strip is one stop in the tab order, not one per tab.
        using var context = new BunitContext();

        var tabs = context.Render<TabsHarness>();
        var tabindexes = tabs.FindAll("[role='tab']").Select(tab => tab.GetAttribute("tabindex") ?? string.Empty).ToArray();

        Assert.Equal(["0", "-1", "-1"], tabindexes);
    }

    [Fact]
    public void Arrow_keys_move_the_active_tab_and_wrap_around()
    {
        using var context = new BunitContext();

        var tabs = context.Render<TabsHarness>();

        tabs.FindAll("[role='tab']")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.Contains("Second body", tabs.Markup, StringComparison.Ordinal);
        Assert.Equal("two", tabs.Instance.LastActiveId);

        tabs.FindAll("[role='tab']")[1].KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        Assert.Contains("First body", tabs.Markup, StringComparison.Ordinal);

        tabs.FindAll("[role='tab']")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        Assert.Contains("Third body", tabs.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// The ring goes with the selection.
    /// <para>
    /// Roving tabindex means the tab that was active is the one at
    /// <c>tabindex="-1"</c> the moment the reader arrows off it. Leave focus there
    /// and the browser computes the next arrow from a tab that is no longer the
    /// visible one — ArrowRight then ArrowLeft lands back on the second tab, and
    /// there is no key that returns to the first. The strip becomes one-way.
    /// </para>
    /// <para>
    /// What is pinned here is that each move asks for focus, and asks for it
    /// somewhere new. Which element the reference points at is not something a
    /// render test can see — bUnit has no focus — so the identity is the browser's
    /// to check; the call being made at all, once per move, is this one's.
    /// </para>
    /// </summary>
    [Fact]
    public void An_arrow_key_takes_focus_with_it()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid("Blazor._internal.domWrapper.focus", _ => true);

        var tabs = context.Render<TabsHarness>();

        tabs.FindAll("[role='tab']")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        tabs.FindAll("[role='tab']")[1].KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        var focused = context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"];

        Assert.Equal(2, focused.Count);
        Assert.NotEqual(
            ((ElementReference)focused[0].Arguments[0]!).Id,
            ((ElementReference)focused[1].Arguments[0]!).Id);
    }

    /// <summary>Pressing the tab that is already active moves nothing, so it takes
    /// no focus with it either: the reader is already there.</summary>
    [Fact]
    public void Clicking_a_tab_leaves_the_focus_where_the_reader_put_it()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid("Blazor._internal.domWrapper.focus", _ => true);

        var tabs = context.Render<TabsHarness>();

        tabs.FindAll("[role='tab']")[1].Click();

        Assert.Empty(context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"]);
    }

    [Fact]
    public void Home_and_End_jump_to_the_ends_of_the_strip()
    {
        using var context = new BunitContext();

        var tabs = context.Render<TabsHarness>();

        tabs.FindAll("[role='tab']")[0].KeyDown(new KeyboardEventArgs { Key = "End" });
        Assert.Equal("three", tabs.Instance.LastActiveId);

        tabs.FindAll("[role='tab']")[2].KeyDown(new KeyboardEventArgs { Key = "Home" });
        Assert.Equal("one", tabs.Instance.LastActiveId);
    }

    [Fact]
    public void Clicking_a_tab_activates_it()
    {
        using var context = new BunitContext();

        var tabs = context.Render<TabsHarness>();

        tabs.FindAll("[role='tab']")[2].Click();

        Assert.Equal("three", tabs.Instance.LastActiveId);
        Assert.Contains("Third body", tabs.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_class_hooks_replace_the_defaults_instead_of_appending_to_them()
    {
        // A host that already ships its own tab strip has to be able to hand over
        // the whole class name, or it ends up styling against ours as well.
        using var context = new BunitContext();

        var tabs = context.Render<TabsHarness>(parameters => parameters
            .Add(h => h.ListCssClass, "pill-row")
            .Add(h => h.TabCssClass, "pill")
            .Add(h => h.ActiveTabCssClass, "pill pill--on"));

        Assert.Equal("pill-row", tabs.Find("[role='tablist']").GetAttribute("class"));
        Assert.Equal("pill pill--on", tabs.FindAll("[role='tab']")[0].GetAttribute("class"));
        Assert.Equal("pill", tabs.FindAll("[role='tab']")[1].GetAttribute("class"));
        Assert.Empty(tabs.FindAll(".tabs__tab"));
    }

    [Fact]
    public void A_panel_can_be_rendered_as_another_element_under_another_class()
    {
        using var context = new BunitContext();

        var panel = context.Render<Tabs>(parameters => parameters
            .Add(t => t.ActiveId, "only")
            .AddChildContent<TabPanel>(child => child
                .Add(p => p.Id, "only")
                .Add(p => p.Title, "Only")
                .Add(p => p.Element, "section")
                .Add(p => p.BaseClass, "host-panel")));

        var element = panel.Find("[role='tabpanel']");

        Assert.Equal("section", element.NodeName.ToLowerInvariant());
        Assert.Equal("host-panel", element.GetAttribute("class"));
    }
}
