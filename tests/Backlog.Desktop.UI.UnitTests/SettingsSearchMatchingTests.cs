using Backlog.UI.Components.Settings;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the settings search matches: page titles and the card titles each page
/// declares, ignoring case - never anything a page draws - and what the menu says
/// when nothing matches.
/// </summary>
public sealed class SettingsSearchMatchingTests
{
    private static readonly IReadOnlyList<SettingsSection> Pages =
    [
        new("storage", "Storage & backup", 0, typeof(object), Group: SettingsGroup.Workspace,
            CardTitles: ["Where the backlog is stored", "Backup", "Brain"]),
        new("repositories", "Repositories", 1, typeof(object), Group: SettingsGroup.Workspace,
            CardTitles: ["Repositories"]),
        new("ai", "AI providers", 0, typeof(object), Group: SettingsGroup.Ai,
            CardTitles: ["Claude usage API", "GitHub usage API"]),
        new("plain", "Plain", 0, typeof(object)),
    ];

    [Fact]
    public void Blank_text_keeps_every_page_with_no_cards()
    {
        foreach (var text in new[] { null, "", "   " })
        {
            var matches = SettingsPages.Find(Pages, text);

            Assert.Equal(Pages.Select(page => page.Id), matches.Select(match => match.Page.Id));
            Assert.All(matches, match => Assert.Empty(match.Cards));
        }
    }

    [Fact]
    public void A_page_title_matches_ignoring_case_and_lists_no_card_it_does_not_hold()
    {
        var match = Assert.Single(SettingsPages.Find(Pages, "STORAGE"));

        Assert.Equal("storage", match.Page.Id);
        Assert.Empty(match.Cards);
    }

    [Fact]
    public void A_card_title_keeps_its_page_and_is_listed_under_it()
    {
        var match = Assert.Single(SettingsPages.Find(Pages, "backup"));

        Assert.Equal("storage", match.Page.Id);
        Assert.Equal(["Backup"], match.Cards);
    }

    [Fact]
    public void Every_card_that_holds_the_text_is_listed_in_the_pages_order()
    {
        var match = Assert.Single(SettingsPages.Find(Pages, "usage api"));

        Assert.Equal("ai", match.Page.Id);
        Assert.Equal(["Claude usage API", "GitHub usage API"], match.Cards);
    }

    [Fact]
    public void Pages_keep_their_menu_order_and_a_page_matching_by_title_and_card_lists_the_card()
    {
        var matches = SettingsPages.Find(Pages, "repo");

        Assert.Equal(["repositories"], matches.Select(match => match.Page.Id));
        Assert.Equal(["Repositories"], matches[0].Cards);
    }

    [Fact]
    public void A_page_that_declares_no_cards_matches_on_its_title_alone()
    {
        Assert.Equal(["plain"], SettingsPages.Find(Pages, "lai").Select(match => match.Page.Id));
        Assert.DoesNotContain(SettingsPages.Find(Pages, "stored"), match => match.Page.Id == "plain");
    }

    [Fact]
    public void A_switched_off_page_is_found_by_its_title_but_not_by_its_cards()
    {
        bool SwitchedOff(SettingsSection page) => page.Id == "storage";

        var byTitle = Assert.Single(SettingsPages.Find(Pages, "storage", SwitchedOff));
        Assert.Equal("storage", byTitle.Page.Id);
        Assert.Empty(byTitle.Cards);

        Assert.Empty(SettingsPages.Find(Pages, "brain", SwitchedOff));
    }

    [Fact]
    public void Surrounding_spaces_are_not_part_of_the_text()
    {
        Assert.Equal(["storage"], SettingsPages.Find(Pages, "  brain ").Select(match => match.Page.Id));
    }

    [Fact]
    public void Text_nothing_holds_keeps_no_page()
    {
        Assert.Empty(SettingsPages.Find(Pages, "zebra"));
    }

    [Fact]
    public void The_empty_state_quotes_the_text()
    {
        Assert.Equal("No setting matches \"zebra\".", SettingsPages.NoMatch(" zebra "));
    }

    [Fact]
    public void A_card_is_addressed_by_its_page_and_a_fragment_made_of_its_title()
    {
        Assert.Equal("card-where-the-backlog-is-stored", SettingsPages.CardFragment("Where the backlog is stored"));
        Assert.Equal("card-claude-usage-api", SettingsPages.CardFragment("Claude usage API"));
        Assert.Equal("settings/storage#card-backup", SettingsPages.CardHref("storage", "Backup"));
    }

    [Fact]
    public void A_fragment_names_a_card_only_among_the_cards_its_page_declares()
    {
        var storage = Pages[0];

        Assert.Equal("Backup", SettingsPages.CardNamedBy(storage, "#card-backup"));
        Assert.Null(SettingsPages.CardNamedBy(storage, "#card-claude-usage-api"));
        Assert.Null(SettingsPages.CardNamedBy(storage, "#brain-folder"));
        Assert.Null(SettingsPages.CardNamedBy(storage, null));
    }

    [Fact]
    public void Every_built_in_card_has_its_own_fragment_on_its_page()
    {
        Assert.All(SettingsPages.BuiltIn, page =>
        {
            var fragments = page.CardTitles!.Select(SettingsPages.CardFragment).ToList();
            Assert.Equal(fragments.Count, fragments.Distinct(StringComparer.Ordinal).Count());
        });
    }
}
