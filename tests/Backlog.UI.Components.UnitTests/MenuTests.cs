namespace Backlog.UI.Components.UnitTests;

public sealed class MenuListTests
{
    private static readonly MenuItem[] Items =
    [
        new("open", "Open"),
        new("rename", "Rename"),
        MenuItem.Divider("sep"),
        new("archive", "Archive", Disabled: true),
        new("delete", "Delete", Destructive: true)
    ];

    [Fact]
    public void The_menu_is_a_menu_of_menuitems_with_separators_between_them()
    {
        using var context = new BunitContext();

        var menu = context.Render<MenuList>(parameters => parameters
            .Add(m => m.Items, Items)
            .Add(m => m.AriaLabel, "Entry actions"));

        Assert.Equal("Entry actions", menu.Find("[role='menu']").GetAttribute("aria-label"));
        Assert.Equal(4, menu.FindAll("[role='menuitem']").Count);
        Assert.Single(menu.FindAll("[role='separator']"));
        Assert.Contains("menu-list__item--destructive", menu.FindAll("[role='menuitem']").Last().ClassList);
    }

    [Fact]
    public void The_menu_is_one_tab_stop()
    {
        using var context = new BunitContext();

        var menu = context.Render<MenuList>(parameters => parameters.Add(m => m.Items, Items));
        var tabbable = menu.FindAll("[role='menuitem']").Where(item => item.GetAttribute("tabindex") == "0").ToArray();

        Assert.Single(tabbable);
        Assert.Equal("Open", tabbable[0].TextContent.Trim());
    }

    [Fact]
    public void Arrow_keys_walk_past_separators_and_disabled_rows()
    {
        using var context = new BunitContext();

        var menu = context.Render<MenuList>(parameters => parameters.Add(m => m.Items, Items));

        menu.FindAll("[role='menuitem']")[1].KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        var tabbable = menu.FindAll("[role='menuitem']").Single(item => item.GetAttribute("tabindex") == "0");
        Assert.Equal("Delete", tabbable.TextContent.Trim());
    }

    /// <summary>
    /// Enter on a focused button is a keydown and then the click the browser
    /// raises for it, and nothing may refuse that click: the first key a menu
    /// sees is often Enter, straight after FocusFirstItem put the focus on the
    /// first item. So the row is chosen once, by the click, and never a second
    /// time by the keydown in front of it.
    /// </summary>
    [Theory]
    [InlineData("Enter")]
    [InlineData(" ")]
    public void A_key_that_activates_the_focused_item_chooses_it_once(string key)
    {
        using var context = new BunitContext();
        var selected = new List<string>();

        var menu = context.Render<MenuList>(parameters => parameters
            .Add(m => m.Items, Items)
            .Add(m => m.FocusFirstItem, true)
            .Add(m => m.OnItemSelected, (MenuItem item) => selected.Add(item.Id)));

        var first = menu.FindAll("[role='menuitem']")[0];
        Assert.Equal(first.GetAttribute("blazor:elementReference"), FocusedReference(context).Id);

        first.KeyDown(new KeyboardEventArgs { Key = key });
        menu.FindAll("[role='menuitem']")[0].Click();

        Assert.Equal(["open"], selected);
    }

    /// <summary>
    /// The keydown never refuses the default of the key it is handed, so the
    /// click Enter raises can never be the one a previous arrow's flag
    /// swallowed: the browser, not the server, decides the arrows' default.
    /// </summary>
    [Fact]
    public void Menu_items_bind_no_server_side_prevent_default()
    {
        using var context = new BunitContext();

        var menu = context.Render<MenuList>(parameters => parameters.Add(m => m.Items, Items));

        menu.FindAll("[role='menuitem']")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        Assert.DoesNotContain("preventDefault", menu.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_disabled_row_is_never_chosen()
    {
        using var context = new BunitContext();
        var selected = new List<string>();

        var menu = context.Render<MenuList>(parameters => parameters
            .Add(m => m.Items, Items)
            .Add(m => m.OnItemSelected, (MenuItem item) => selected.Add(item.Id)));

        menu.FindAll("[role='menuitem']")[2].KeyDown(new KeyboardEventArgs { Key = "Enter" });
        menu.FindAll("[role='menuitem']")[2].Click();

        Assert.Empty(selected);
    }

    /// <summary>
    /// A host that opens a menu for the keyboard asks for the first item to be
    /// focused, so the next arrow or Enter lands in the menu (WAI-ARIA menu
    /// pattern). The first item that can be activated, that is: a disabled row
    /// or a separator at the top is skipped rather than focused and refused.
    /// </summary>
    [Fact]
    public void Focus_first_item_focuses_the_first_enabled_item_which_is_the_tab_stop()
    {
        using var context = new BunitContext();
        MenuItem[] items =
        [
            new("archive", "Archive", Disabled: true),
            MenuItem.Divider("sep"),
            new("open", "Open"),
            new("delete", "Delete")
        ];

        var menu = context.Render<MenuList>(parameters => parameters
            .Add(m => m.Items, items)
            .Add(m => m.FocusFirstItem, true));

        var focused = FocusedReference(context);
        var open = menu.FindAll("[role='menuitem']").Single(item => item.TextContent.Trim() == "Open");

        Assert.Equal(open.GetAttribute("blazor:elementReference"), focused.Id);
        Assert.Equal("0", open.GetAttribute("tabindex"));
    }

    /// <summary>A menu rendered inline is one tab stop among the page's, and
    /// must not pull the focus to itself just by appearing.</summary>
    [Fact]
    public void Without_focus_first_item_rendering_the_menu_moves_no_focus()
    {
        using var context = new BunitContext();

        context.Render<MenuList>(parameters => parameters.Add(m => m.Items, Items));

        Assert.DoesNotContain(context.JSInterop.Invocations, IsFocus);
    }

    internal static ElementReference FocusedReference(BunitContext context)
    {
        var invocation = Assert.Single(context.JSInterop.Invocations, IsFocus);
        return Assert.IsType<ElementReference>(invocation.Arguments[0]);
    }

    /// <summary>What <c>ElementReference.FocusAsync</c> goes out as.</summary>
    private static bool IsFocus(JSRuntimeInvocation invocation) =>
        invocation.Identifier.EndsWith(".focus", StringComparison.Ordinal);
}

public sealed class ContextMenuTests
{
    private static readonly MenuItem[] Items = [new("open", "Open"), new("delete", "Delete")];

    [Fact]
    public void A_closed_context_menu_puts_nothing_in_the_document()
    {
        using var context = new BunitContext();

        var menu = context.Render<ContextMenu>(parameters => parameters.Add(m => m.Items, Items));

        Assert.Equal(string.Empty, menu.Markup.Trim());
    }

    [Fact]
    public void An_open_context_menu_is_a_menu_placed_where_it_was_asked_for()
    {
        using var context = new BunitContext();

        var menu = context.Render<ContextMenu>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.X, 12)
            .Add(m => m.Y, 34)
            .Add(m => m.Items, Items));

        Assert.Equal(2, menu.FindAll("[role='menuitem']").Count);
        Assert.Contains("--context-menu-x: 12px", menu.Find(".context-menu").GetAttribute("style"), StringComparison.Ordinal);
        Assert.Contains("--context-menu-y: 34px", menu.Find(".context-menu").GetAttribute("style"), StringComparison.Ordinal);
    }

    [Fact]
    public void Escape_closes_the_context_menu()
    {
        using var context = new BunitContext();
        bool? open = null;

        var menu = context.Render<ContextMenu>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.Items, Items)
            .Add(m => m.OpenChanged, (bool value) => open = value));

        menu.Find(".context-menu__backdrop").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.False(open);
    }

    /// <summary>
    /// Opening a context menu puts the focus on its first item, so a keyboard
    /// user can press ArrowDown or Enter straight away instead of Tabbing into
    /// the menu first.
    /// </summary>
    [Fact]
    public void Opening_the_context_menu_focuses_its_first_item()
    {
        using var context = new BunitContext();

        var menu = context.Render<ContextMenu>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.Items, Items));

        var focused = MenuListTests.FocusedReference(context);
        var first = menu.FindAll("[role='menuitem']")[0];

        Assert.Equal(first.GetAttribute("blazor:elementReference"), focused.Id);
        Assert.Equal("0", first.GetAttribute("tabindex"));
    }

    // In a real browser a keydown is delivered to the focused element — now the
    // first item — and bubbles from there, so Escape pressed on an item has to
    // reach the backdrop's handler.
    [Fact]
    public void Escape_pressed_on_a_focused_item_closes_the_context_menu()
    {
        using var context = new BunitContext();
        bool? open = null;

        var menu = context.Render<ContextMenu>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.Items, Items)
            .Add(m => m.OpenChanged, (bool value) => open = value));

        menu.FindAll("[role='menuitem']")[0].KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.False(open);
    }

    // A menu with nothing to focus still has to hear Escape, so the backdrop
    // stays focusable and takes the focus itself in that one case.
    [Fact]
    public void A_context_menu_with_no_enabled_item_focuses_its_backdrop_so_escape_reaches_it()
    {
        using var context = new BunitContext();

        var menu = context.Render<ContextMenu>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.Items, [new MenuItem("archive", "Archive", Disabled: true)]));

        var backdrop = menu.Find(".context-menu__backdrop");
        var focused = MenuListTests.FocusedReference(context);

        Assert.Equal("-1", backdrop.GetAttribute("tabindex"));
        Assert.Equal(backdrop.GetAttribute("blazor:elementReference"), focused.Id);
    }

    [Fact]
    public void Choosing_an_item_reports_it_and_closes_the_menu()
    {
        using var context = new BunitContext();
        MenuItem? selected = null;
        bool? open = null;

        var menu = context.Render<ContextMenu>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.Items, Items)
            .Add(m => m.OnItemSelected, (MenuItem item) => selected = item)
            .Add(m => m.OpenChanged, (bool value) => open = value));

        menu.FindAll("[role='menuitem']")[1].Click();

        Assert.Equal("delete", selected?.Id);
        Assert.False(open);
    }

    [Fact]
    public void Clicking_outside_the_menu_closes_it()
    {
        // The outside-click surface is a real element rather than a document
        // listener, so it disappears with the menu and cannot be left behind.
        using var context = new BunitContext();
        bool? open = null;

        var menu = context.Render<ContextMenu>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.Items, Items)
            .Add(m => m.OpenChanged, (bool value) => open = value));

        menu.Find(".context-menu__backdrop").Click();

        Assert.False(open);
    }
}
