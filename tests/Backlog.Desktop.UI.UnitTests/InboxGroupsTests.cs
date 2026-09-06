namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The drawers the inbox is read as, computed without a DOM.
///
/// <para>PARA is the structure and a lens only splits what is inside a drawer.
/// Four rules carry it. Drawers keep PARA's order with Unsorted last, and a
/// drawer over nothing is not emitted. Under no lens a drawer is what it is made
/// of — Projects per project, Areas per area, the rest one list. A fallback
/// section — Untagged, No repository, No area — is last and only present when
/// something is in it. And an item with three tags is in three sections,
/// because a tag is a lens and not a drawer.</para>
/// </summary>
public sealed class InboxGroupsTests
{
    private static InboxItem Item(string key, string? area = null, InboxItemKind kind = InboxItemKind.Text,
        string[]? tags = null, string? repo = null, ParaCategory? para = null) =>
        new(key, "Item " + key, area)
        {
            Kind = kind,
            Tags = tags ?? [],
            Repository = repo,
            Para = para
        };

    [Fact]
    public void Nothing_to_show_is_no_drawers_at_all()
    {
        Assert.Empty(InboxGroups.Build([], InboxGrouping.None));
        Assert.Empty(InboxGroups.Build([], InboxGrouping.Tag));
        Assert.Empty(InboxGroups.Build([], InboxGrouping.Repository));
    }

    [Fact]
    public void Drawers_keep_paras_order_and_say_unsorted_last()
    {
        var items = new[]
        {
            Item("r1", para: ParaCategory.Resources),
            Item("u1"),
            Item("p1", repo: "JSdotNet/Backlog", para: ParaCategory.Projects),
            Item("a1", area: "Platform", para: ParaCategory.Areas),
            Item("r2", para: ParaCategory.Resources),
            Item("x1", para: ParaCategory.Archive)
        };

        var drawers = InboxGroups.Build(items, InboxGrouping.None);

        Assert.Equal(["Projects", "Areas", "Resources", "Archive", "Unsorted"], drawers.Select(drawer => drawer.Name));
        Assert.Equal([1, 1, 2, 1, 1], drawers.Select(drawer => drawer.Items.Count));
        Assert.Equal(InboxGroups.UnsortedKey, drawers[^1].Key);
    }

    [Fact]
    public void An_empty_drawer_is_not_drawn()
    {
        var drawers = InboxGroups.Build([Item("r", para: ParaCategory.Resources)], InboxGrouping.None);

        var only = Assert.Single(drawers);
        Assert.Equal("Resources", only.Name);
    }

    [Fact]
    public void Under_no_lens_projects_are_sectioned_per_project_and_areas_per_area()
    {
        var items = new[]
        {
            Item("p2", repo: "JSdotNet/Zeta", para: ParaCategory.Projects),
            Item("p1", repo: "JSdotNet/Backlog", para: ParaCategory.Projects),
            Item("p3", repo: "jsdotnet/backlog", para: ParaCategory.Projects),
            Item("p0", para: ParaCategory.Projects),
            Item("a1", area: "Platform", para: ParaCategory.Areas),
            Item("a2", area: "Admin", para: ParaCategory.Areas),
            Item("r1", para: ParaCategory.Resources),
            Item("u1")
        };

        var drawers = InboxGroups.Build(items, InboxGrouping.None);

        var projects = drawers[0];
        Assert.True(projects.IsSectioned);
        Assert.Equal(["JSdotNet/Backlog", "JSdotNet/Zeta", "No repository"], projects.Sections.Select(section => section.Name));
        Assert.Equal(["p1", "p3"], projects.Sections[0].Items.Select(item => item.Key));
        Assert.Equal("projects-repo-jsdotnet-backlog", projects.Sections[0].Key);
        Assert.Equal(4, projects.Items.Count);

        var areas = drawers[1];
        Assert.Equal(["Admin", "Platform"], areas.Sections.Select(section => section.Name));
        Assert.Equal("areas-area-admin", areas.Sections[0].Key);

        Assert.False(drawers[2].IsSectioned);
        Assert.False(drawers[3].IsSectioned);
    }

    [Fact]
    public void An_area_drawer_names_the_items_with_no_area_last()
    {
        var items = new[]
        {
            Item("a0", para: ParaCategory.Areas),
            Item("a1", area: "Platform", para: ParaCategory.Areas)
        };

        var areas = Assert.Single(InboxGroups.Build(items, InboxGrouping.None));

        Assert.Equal(["Platform", "No area"], areas.Sections.Select(section => section.Name));
        Assert.Equal("areas-" + InboxGroups.NoAreaKey, areas.Sections[^1].Key);
    }

    [Fact]
    public void The_tag_lens_puts_an_item_under_each_of_its_tags_inside_its_drawer()
    {
        var items = new[]
        {
            Item("one", tags: ["sync", "Aspire"], para: ParaCategory.Resources),
            Item("two", tags: ["aspire"], para: ParaCategory.Resources),
            Item("three", para: ParaCategory.Resources),
            Item("four", tags: ["aspire"])
        };

        var drawers = InboxGroups.Build(items, InboxGrouping.Tag);

        var resources = drawers[0];
        Assert.Equal(["#Aspire", "#sync", "Untagged"], resources.Sections.Select(section => section.Name));
        Assert.Equal(["one", "two"], resources.Sections[0].Items.Select(item => item.Key));
        Assert.Equal(["one"], resources.Sections[1].Items.Select(item => item.Key));
        Assert.Equal(["three"], resources.Sections[2].Items.Select(item => item.Key));
        Assert.Equal("resources-tag-aspire", resources.Sections[0].Key);
        Assert.Equal(3, resources.Items.Count);

        // The tag in another drawer is another section: a lens never merges drawers.
        var unsorted = drawers[1];
        Assert.Equal(["#aspire"], unsorted.Sections.Select(section => section.Name));
        Assert.Equal("unsorted-tag-aspire", unsorted.Sections[0].Key);
    }

    [Fact]
    public void A_stored_hash_or_a_blank_tag_does_not_make_a_second_section()
    {
        var items = new[] { Item("one", tags: ["#sync", "sync", " "]) };

        var drawer = Assert.Single(InboxGroups.Build(items, InboxGrouping.Tag));

        var only = Assert.Single(drawer.Sections);
        Assert.Equal("#sync", only.Name);
        Assert.Single(only.Items);
    }

    [Fact]
    public void The_repository_lens_sections_every_drawer_alphabetically_with_the_unscoped_items_last()
    {
        var items = new[]
        {
            Item("z", repo: "JSdotNet/Zeta", para: ParaCategory.Resources),
            Item("n", para: ParaCategory.Resources),
            Item("a", repo: "JSdotNet/Alpha", para: ParaCategory.Resources),
            Item("a2", repo: "jsdotnet/alpha", para: ParaCategory.Resources)
        };

        var resources = Assert.Single(InboxGroups.Build(items, InboxGrouping.Repository));

        Assert.Equal(["JSdotNet/Alpha", "JSdotNet/Zeta", "No repository"], resources.Sections.Select(section => section.Name));
        Assert.Equal(["a", "a2"], resources.Sections[0].Items.Select(item => item.Key));
        Assert.Equal("resources-repo-jsdotnet-alpha", resources.Sections[0].Key);
        Assert.Equal("resources-" + InboxGroups.NoRepositoryKey, resources.Sections[^1].Key);
    }

    [Theory]
    [InlineData("JSdotNet/Backlog", "jsdotnet-backlog")]
    [InlineData("  Some Name!! ", "some-name")]
    [InlineData("///", "group")]
    public void A_key_is_safe_for_an_element_id(string name, string expected)
    {
        Assert.Equal(expected, InboxGroups.Slug(name));
    }
}
