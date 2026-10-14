using Backlog.UI.Components.Settings;

using Bunit;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The settings search field and the card links a search lists in the menu. What
/// the field matches is the host's; these pin what the field says, what it
/// reports, and that Escape empties it.
/// </summary>
public sealed class SettingsSearchTests
{
    [Fact]
    public void The_field_is_labelled_Find_a_setting_out_of_sight_and_shows_the_same_words()
    {
        using var context = new BunitContext();

        var search = context.Render<SettingsSearch>();

        var input = search.Find("input[type='search']");
        var label = search.Find("label");
        Assert.Equal("Find a setting", input.GetAttribute("placeholder"));
        Assert.Equal("Find a setting", label.TextContent.Trim());
        Assert.Contains("sr-only", label.ClassList);
        Assert.Equal(input.Id, label.GetAttribute("for"));
        Assert.Equal("search", search.Find(".settings-search").GetAttribute("role"));
        Assert.Equal("true", search.Find("svg.settings-search__icon").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void Typing_reports_every_keystroke()
    {
        using var context = new BunitContext();
        var reported = new List<string>();

        var search = context.Render<SettingsSearch>(parameters => parameters
            .Add(s => s.ValueChanged, (string value) => reported.Add(value)));

        search.Find("input").Input("ba");

        Assert.Equal(["ba"], reported);
    }

    [Fact]
    public void Escape_clears_the_field()
    {
        using var context = new BunitContext();
        string? reported = null;

        var search = context.Render<SettingsSearch>(parameters => parameters
            .Add(s => s.Value, "backup")
            .Add(s => s.ValueChanged, (string value) => reported = value));

        search.Find("input").KeyDown("Escape");

        Assert.Equal(string.Empty, reported);
        Assert.Equal(string.Empty, search.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void A_menu_item_lists_the_cards_a_search_matched_as_links_under_it()
    {
        using var context = new BunitContext();

        var group = context.Render<SettingsMenuGroup>(parameters => parameters
            .Add(g => g.Label, "Workspace")
            .Add(g => g.Items, [
                new SettingsMenuItem("storage", "Storage & backup", "settings/storage",
                    Cards: [new SettingsMenuCardLink("Backup", "settings/storage#card-backup")]),
                new SettingsMenuItem("features", "Features", "settings/features"),
            ]));

        var card = group.Find("[data-testid='settings-menu-card']");
        Assert.Equal("Backup", card.TextContent.Trim());
        Assert.Equal("settings/storage#card-backup", card.GetAttribute("href"));
        Assert.Single(group.FindAll(".settings-menu-group__cards"));
        Assert.Equal("Matching cards on Storage & backup", group.Find(".settings-menu-group__cards").GetAttribute("aria-label"));
    }
}
