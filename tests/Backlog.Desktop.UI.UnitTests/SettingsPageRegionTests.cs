namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A Settings page draws no panel of its own: <c>SettingsFrame</c> is the one panel,
/// and each page's <c>.setting</c> region sits inside its content slot unframed, so
/// a page's cards rest directly on it. <c>.setting</c> keeps its panel elsewhere -
/// the Instructions screen still uses it as one - so the reset is scoped to the
/// frame's content slot rather than taken off the class.
/// </summary>
public sealed class SettingsPageRegionTests
{
    [Fact]
    public void A_settings_page_region_draws_no_border_radius_padding_or_surface()
    {
        var region = Block(Css(), ".settings-frame__content > .setting {");

        Assert.Contains("border: 0;", region, StringComparison.Ordinal);
        Assert.Contains("border-radius: 0;", region, StringComparison.Ordinal);
        Assert.Contains("padding: 0;", region, StringComparison.Ordinal);
        Assert.Contains("background: none;", region, StringComparison.Ordinal);
    }

    [Fact]
    public void The_setting_panel_outside_the_frame_keeps_its_border()
    {
        var panel = Block(Css(), "\n.setting {");

        Assert.Contains("border: var(--border-width) solid var(--color-border);", panel, StringComparison.Ordinal);
    }

    private static string Css() => File.ReadAllText(
        RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css")).Replace("\r\n", "\n");

    private static string Block(string css, string opening)
    {
        var start = css.IndexOf(opening, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{opening.Trim()} should exist.");

        var end = css.IndexOf('}', start);
        return css[start..(end + 1)];
    }
}
