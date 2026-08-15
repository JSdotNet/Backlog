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

    [Fact]
    public void Enter_activates_the_row_under_the_cursor_but_never_a_disabled_one()
    {
        using var context = new BunitContext();
        var selected = new List<string>();

        var menu = context.Render<MenuList>(parameters => parameters
            .Add(m => m.Items, Items)
            .Add(m => m.OnItemSelected, (MenuItem item) => selected.Add(item.Id)));

        menu.FindAll("[role='menuitem']")[0].KeyDown(new KeyboardEventArgs { Key = "Enter" });
        menu.FindAll("[role='menuitem']")[2].KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(["open"], selected);
    }
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

    // bUnit dispatches keydown straight at the element, so it cannot notice that
    // a real browser delivers the key to the focused element instead. The
    // backdrop must therefore be focusable for Escape to ever reach it.
    [Fact]
    public void Context_menu_backdrop_is_focusable_so_escape_reaches_it()
    {
        using var context = new BunitContext();

        var menu = context.Render<ContextMenu>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.Items, Items));

        Assert.Equal("-1", menu.Find(".context-menu__backdrop").GetAttribute("tabindex"));
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
