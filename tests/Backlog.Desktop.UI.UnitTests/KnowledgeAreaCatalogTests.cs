using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class KnowledgeAreaCatalogTests
{
    [Fact]
    public void Hides_every_knowledge_area_when_all_folders_are_disabled()
    {
        var folders = KnowledgeFolderSetting.Defaults()
            .Select(folder => folder with { Enabled = false });

        var areas = KnowledgeAreaCatalog.VisibleAreas(folders);

        Assert.Empty(areas);
    }

    [Fact]
    public void Shows_enabled_knowledge_areas()
    {
        var folders = KnowledgeFolderSetting.Defaults()
            .Select(folder => folder with
            {
                Enabled = folder.Key is ".domain" or ".design"
            });

        var areas = KnowledgeAreaCatalog.VisibleAreas(folders);

        Assert.Equal(["domain", "design"], areas.Select(area => area.Key));
    }

    /// <summary>
    /// Backlog Management is its own workspace section, not a knowledge-base one.
    /// A settings file written before it was retired still names <c>.backlog</c>,
    /// and switched on at that: the area strip must stay silent about it rather
    /// than offer a tab whose only content was ever "look somewhere else".
    /// </summary>
    [Fact]
    public void Never_shows_a_backlog_area_even_when_a_stale_setting_enables_one()
    {
        KnowledgeFolderSetting[] folders =
        [
            .. KnowledgeFolderSetting.Defaults(),
            new(".backlog", "Backlog", ".backlog") { Enabled = true }
        ];

        var areas = KnowledgeAreaCatalog.VisibleAreas(folders);

        Assert.DoesNotContain("backlog", areas.Select(area => area.Key));
    }

    [Fact]
    public void Publishes_no_backlog_folder_of_its_own()
    {
        Assert.DoesNotContain(".backlog", KnowledgeFolderSetting.Defaults().Select(folder => folder.Key));
    }
}
