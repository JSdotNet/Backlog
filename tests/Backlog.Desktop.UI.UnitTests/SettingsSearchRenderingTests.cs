using Bunit;

using static Backlog.Desktop.UI.UnitTests.SettingsSectionRenderingTests;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The header's Find a setting field on the Settings screen: it narrows the menu
/// to the pages it matches, hides a group it left empty, lists a matched card
/// under its page, says so when nothing matches, and a card link brings the card
/// into view. The matching itself is pinned in <see cref="SettingsSearchMatchingTests"/>;
/// the screen is rendered the way <see cref="SettingsSectionRenderingTests"/> renders it.
/// </summary>
public sealed class SettingsSearchRenderingTests
{
    private const string Groups = "nav[aria-label='Settings pages'] .settings-menu-group__label";

    [Fact]
    public void The_header_search_slot_holds_the_Find_a_setting_field()
    {
        using var settings = RenderSettings();

        var slot = settings.Component.Find("[data-testid='settings-search-slot']");
        var input = slot.QuerySelector("input[type='search']")!;
        Assert.Equal("Find a setting", input.GetAttribute("placeholder"));
        Assert.Equal("Find a setting", slot.QuerySelector("label.sr-only")!.TextContent.Trim());
    }

    [Fact]
    public void Typing_keeps_only_the_matching_pages_and_hides_a_group_left_empty()
    {
        using var settings = RenderSettings(
            new SettingsSection("pricing", "Pricing", 1, typeof(SoonerSection), Group: SettingsGroup.Ai),
            new SettingsSection("area", "Area", 10, typeof(SoonerSection), CardTitles: ["Backups elsewhere"]));

        Search(settings.Component, "BACKUP");

        Assert.Equal(["Storage & backup", "Area"], SettingsMenu.Pages(settings.Component));
        Assert.Equal(["Workspace", "Product areas"], settings.Component.FindAll(Groups).Select(label => label.TextContent.Trim()));
    }

    [Fact]
    public void A_matched_card_is_listed_under_its_page_as_a_link_to_the_card()
    {
        using var settings = RenderSettings();

        Search(settings.Component, "backup");

        var cards = settings.Component.FindAll("[data-testid='settings-menu-card']");
        var card = Assert.Single(cards);
        Assert.Equal("Backup", card.TextContent.Trim());
        Assert.Equal("settings/storage#card-backup", card.GetAttribute("href"));
        Assert.NotNull(settings.Component.Find("a[data-page='storage']").ParentElement!.QuerySelector("[data-testid='settings-menu-card']"));
    }

    [Fact]
    public void Nothing_matching_says_so_in_the_menus_place()
    {
        using var settings = RenderSettings();

        Search(settings.Component, "zebra");

        Assert.Equal("No setting matches \"zebra\".", settings.Component.Find("[data-testid='settings-search-empty']").TextContent.Trim());
        Assert.Empty(SettingsMenu.Pages(settings.Component));
        Assert.Empty(settings.Component.FindAll(Groups));
    }

    [Fact]
    public void Escape_clears_the_field_and_brings_the_whole_menu_back()
    {
        using var settings = RenderSettings();
        Search(settings.Component, "zebra");

        settings.Component.Find("[data-testid='settings-search-input']").KeyDown("Escape");

        Assert.Empty(settings.Component.FindAll("[data-testid='settings-search-empty']"));
        Assert.Equal(AppWidePages, SettingsMenu.Pages(settings.Component));
        Assert.Empty(settings.Component.FindAll("[data-testid='settings-menu-card']"));
    }

    [Fact]
    public void Following_a_card_link_opens_its_page_and_brings_the_card_into_view()
    {
        using var settings = RenderSettings();
        var reveal = settings.TestContext.JSInterop.SetupVoid("backlogRevealSettingsCard", _ => true);
        reveal.SetVoidResult();
        Search(settings.Component, "backup");
        var href = settings.Component.Find("[data-testid='settings-menu-card']").GetAttribute("href")!;

        settings.TestContext.Services.GetRequiredService<NavigationManager>().NavigateTo(href);
        SettingsMenu.Navigate(settings.Component, "storage");

        Assert.Equal("Storage & backup", SettingsMenu.Current(settings.Component));
        settings.Component.WaitForAssertion(() =>
            Assert.Equal("Backup", Assert.Single(reveal.Invocations).Arguments[0]));
    }

    [Fact]
    public void A_fragment_that_names_no_card_of_the_page_reveals_nothing()
    {
        using var settings = RenderSettings();

        settings.TestContext.Services.GetRequiredService<NavigationManager>().NavigateTo("settings/storage#card-claude-usage-api");
        SettingsMenu.Navigate(settings.Component, "storage");

        Assert.DoesNotContain(settings.TestContext.JSInterop.Invocations, call => call.Identifier == "backlogRevealSettingsCard");
    }

    private static void Search(IRenderedComponent<Settings> component, string text) =>
        component.Find("[data-testid='settings-search-input']").Input(text);
}
