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
    public void Shows_enabled_knowledge_areas_including_backlog()
    {
        var folders = KnowledgeFolderSetting.Defaults()
            .Select(folder => folder with
            {
                Enabled = folder.Key is ".domain" or ".design" or ".backlog"
            });

        var areas = KnowledgeAreaCatalog.VisibleAreas(folders);

        Assert.Equal(["backlog", "domain", "design"], areas.Select(area => area.Key));
    }
}
