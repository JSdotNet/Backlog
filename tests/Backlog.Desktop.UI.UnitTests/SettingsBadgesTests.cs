namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The words the settings menu draws on the right of a page's name. Words only,
/// so they are pinned here without rendering the screen.
/// </summary>
public sealed class SettingsBadgesTests
{
    private static readonly AppFeatureDefinition[] Catalog =
    [
        new("one", "One", "The first."),
        new("two", "Two", "The second."),
        new("three", "Three", "The third."),
    ];

    [Fact]
    public void Features_reads_how_many_of_the_catalog_are_on()
    {
        Assert.Equal("2/3", SettingsBadges.Features(Catalog, key => key != "two"));
    }

    [Fact]
    public void Features_reads_zero_when_none_is_on()
    {
        Assert.Equal("0/3", SettingsBadges.Features(Catalog, _ => false));
    }

    [Theory]
    [InlineData(false, false, false, "0/3")]
    [InlineData(true, false, false, "1/3")]
    [InlineData(false, true, true, "2/3")]
    [InlineData(true, true, true, "3/3")]
    public void Ai_providers_reads_how_many_of_the_three_are_set_up(bool claude, bool gitHub, bool azureFoundry, string expected)
    {
        Assert.Equal(expected, SettingsBadges.AiProviders(claude, gitHub, azureFoundry));
    }

    [Fact]
    public void A_count_is_its_figure_zero_included()
    {
        Assert.Equal("0", SettingsBadges.Count(0));
        Assert.Equal("12", SettingsBadges.Count(12));
    }

    [Fact]
    public void A_switched_off_page_reads_Off()
    {
        Assert.Equal("Off", SettingsBadges.Off);
    }

    [Fact]
    public void The_note_on_a_switched_off_page_is_named_by_its_feature()
    {
        Assert.Equal("Inbox pane", SettingsBadges.FeatureName(AppFeatures.All, AppFeatures.InboxPane, "Inbox"));
    }

    [Fact]
    public void A_feature_the_catalog_does_not_name_falls_back_to_the_page_title()
    {
        Assert.Equal("Inbox", SettingsBadges.FeatureName(Catalog, "not-in-the-catalog", "Inbox"));
    }
}
