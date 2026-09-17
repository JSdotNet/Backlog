using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class DevbookAreaCatalogTests
{
    [Fact]
    public void Hides_every_knowledge_area_when_all_folders_are_disabled()
    {
        var folders = DevbookFolderSetting.Defaults()
            .Select(folder => folder with { Enabled = false });

        var areas = DevbookAreaCatalog.VisibleAreas(folders);

        Assert.Empty(areas);
    }

    [Fact]
    public void Shows_enabled_knowledge_areas()
    {
        var folders = DevbookFolderSetting.Defaults()
            .Select(folder => folder with
            {
                Enabled = folder.Key is ".domain" or ".design"
            });

        var areas = DevbookAreaCatalog.VisibleAreas(folders);

        Assert.Equal(["domain", "design"], areas.Select(area => area.Key));
    }

    /// <summary>
    /// Tasks is its own workspace section, not a devbook one.
    /// A settings file written before it was retired still names <c>.backlog</c>,
    /// and switched on at that: the area strip must stay silent about it rather
    /// than offer a tab whose only content was ever "look somewhere else".
    /// </summary>
    [Fact]
    public void Never_shows_a_backlog_area_even_when_a_stale_setting_enables_one()
    {
        DevbookFolderSetting[] folders =
        [
            .. DevbookFolderSetting.Defaults(),
            new(".backlog", "Backlog", ".backlog") { Enabled = true }
        ];

        var areas = DevbookAreaCatalog.VisibleAreas(folders);

        Assert.DoesNotContain("backlog", areas.Select(area => area.Key));
    }

    /// <summary>
    /// The AI adoption record is a devbook folder like the other four, and it
    /// reads last: its chapters point into <c>.tech</c> and never the other way,
    /// so the registry comes before the record that links into it.
    /// </summary>
    [Fact]
    public void Offers_the_ai_adoption_record_as_the_last_area()
    {
        var areas = DevbookAreaCatalog.VisibleAreas(DevbookFolderSetting.Defaults());

        Assert.Equal("ai", areas[^1].Key);
        Assert.Equal("AI", areas[^1].Label);
        Assert.Equal(["instructions", "domain", "arc42", "tech", "design", "ai"], areas.Select(area => area.Key));
    }

    /// <summary>
    /// A settings file written before the folder existed names five folders.
    /// The sixth arrives switched on, as every default does, so a repository that
    /// adopts <c>.ai</c> shows it without anyone finding the setting first.
    /// </summary>
    [Fact]
    public void A_settings_file_that_predates_the_ai_folder_gains_it_enabled()
    {
        var stored = DevbookFolderSetting.Defaults().Where(folder => folder.Key != ".ai").ToList();

        var normalized = DevbookFolderSetting.Normalize(stored);

        var ai = Assert.Single(normalized, folder => folder.Key == ".ai");
        Assert.True(ai.Enabled);
        Assert.Equal(".ai", ai.EffectivePath);
        Assert.Contains("ai", DevbookAreaCatalog.VisibleAreas(normalized).Select(area => area.Key));
    }

    [Theory]
    [InlineData(".ai/adoption-map.md", "ai")]
    [InlineData(".ai/02-specify.md#devbook-skills", "ai")]
    [InlineData(".tech/tooling.md#claude-code", "tech")]
    [InlineData(".github/copilot-instructions.md", null)]
    public void A_reference_resolves_to_the_section_its_folder_belongs_to(string path, string? expected)
    {
        Assert.Equal(expected, DevbookAreaCatalog.AreaKeyForPath(path));
    }

    [Fact]
    public void Publishes_no_backlog_folder_of_its_own()
    {
        Assert.DoesNotContain(".backlog", DevbookFolderSetting.Defaults().Select(folder => folder.Key));
    }
}
