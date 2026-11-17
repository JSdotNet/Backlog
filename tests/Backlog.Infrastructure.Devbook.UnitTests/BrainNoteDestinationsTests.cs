using Backlog.Modules.Brain.Abstractions;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// Where a new Brain note may go (local ADR 0054 §2 and §3): the PARA default
/// set with <c>someday</c> after <c>projects</c>, the destination picker's rows
/// in their order, and which folder a requested destination resolves to.
/// </summary>
public sealed class BrainNoteDestinationsTests
{
    private static BrainArea Inbox => BrainConventions.Para[0];

    [Fact]
    public void The_para_default_set_has_eight_areas_with_someday_after_projects()
    {
        Assert.Equal(
            ["Inbox", "Projects", "Someday", "Areas", "Resources", "People", "Journal", "Archive"],
            BrainConventions.Para.Select(area => area.Name));
        Assert.Equal("someday", BrainConventions.Para[2].Folder);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], BrainConventions.Para.Select(area => area.Order));
    }

    /// <summary>A Brain declaring its own areas gets no <c>someday</c> unless it
    /// declares one (local ADR 0054 §3).</summary>
    [Fact]
    public void A_brain_declaring_its_own_areas_gets_no_someday()
    {
        var areas = BrainConventions.Parse("## Areas\n\n- Inbox — `_inbox`\n- `work`\n");

        Assert.Equal(["_inbox", "work"], areas.Select(area => area.Folder));
    }

    [Fact]
    public void The_picker_lists_the_inbox_first_then_the_areas_in_tab_order_with_projects_after_projects()
    {
        var destinations = BrainNoteDestinations.List(
            BrainConventions.Para,
            Inbox,
            ["projects/sync-rewrite.md", "projects/launch/brief.md", "projects/launch/notes.md", "projects/.drafts/x.md"]);

        Assert.Equal(
            ["_inbox", "projects", "projects/launch", "projects/launch", "projects", "someday", "areas", "resources", "people", "journal", "archive"],
            destinations.Select(destination => destination.Folder));
        Assert.Equal(
            ["Inbox", "Projects", "Projects › launch/brief", "Projects › launch/notes", "Projects › sync-rewrite", "Someday"],
            destinations.Take(6).Select(destination => destination.Label));
        Assert.True(destinations[0].IsInbox);
        Assert.Single(destinations, destination => destination.IsInbox);
        Assert.Equal(3, destinations.Count(destination => destination.IsProject));
        Assert.All(destinations.Where(destination => destination.IsProject), destination => Assert.Equal("Projects", destination.Area));
    }

    /// <summary>The inbox is first even when the conventions write it last.</summary>
    [Fact]
    public void A_declared_inbox_written_last_is_still_listed_first()
    {
        var areas = BrainConventions.Parse("## Areas\n\n- `work`\n- Inbox — `incoming`\n");
        var inbox = BrainNoteText.InboxArea(areas);

        var destinations = BrainNoteDestinations.List(areas, inbox);

        Assert.Equal(["incoming", "work"], destinations.Select(destination => destination.Folder));
    }

    [Fact]
    public void A_brain_without_a_projects_area_lists_no_projects()
    {
        var areas = BrainConventions.Parse("## Areas\n\n- Inbox — `_inbox`\n- `work`\n");

        var destinations = BrainNoteDestinations.List(areas, areas[0], ["projects/sync.md"]);

        Assert.DoesNotContain(destinations, destination => destination.IsProject);
    }

    [Theory]
    [InlineData("projects/sync-rewrite.md", "projects")]
    [InlineData("projects/launch/brief.md", "projects/launch")]
    [InlineData("projects/a/b/c.md", "projects/a/b")]
    [InlineData("Projects/sync.md", "Projects")]
    [InlineData("projects/readme.txt", null)]
    [InlineData("projects/.md", null)]
    [InlineData("areas/health.md", null)]
    [InlineData("projects", null)]
    [InlineData("../projects/x.md", null)]
    public void A_project_is_filed_into_its_folder_beside_its_note(string note, string? expected)
    {
        Assert.Equal(expected, BrainNoteDestinations.ProjectFolderFor("projects", note));
    }

    [Theory]
    [InlineData("someday", "someday")]
    [InlineData("SOMEDAY", "someday")]
    [InlineData("projects/sync-rewrite", "projects/sync-rewrite")]
    [InlineData("projects\\sync-rewrite", "projects/sync-rewrite")]
    [InlineData("_inbox", "_inbox")]
    [InlineData("meetings", null)]
    [InlineData("../outside", null)]
    [InlineData("projects/../../outside", null)]
    [InlineData("projects/.hidden", null)]
    [InlineData("/projects", null)]
    [InlineData("C:/projects", null)]
    [InlineData("projects//x", null)]
    [InlineData("   ", null)]
    public void A_destination_resolves_only_to_an_area_or_a_folder_below_one(string folder, string? expected)
    {
        Assert.Equal(expected, BrainNoteDestinations.Resolve(folder, BrainConventions.Para, Inbox));
    }
}
