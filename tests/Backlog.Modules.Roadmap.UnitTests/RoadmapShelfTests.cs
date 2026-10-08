using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// The shelf rule the roadmap band and the Tasks Calendar share: a plan is waiting until
/// an item carries its tag, compared the way the module stores a tag.
/// </summary>
public class RoadmapShelfTests
{
    private static ImportedPlanDto Imported(string tag) => new(tag, ["backlog"], 2, 5, 0);

    private static RoadmapItemDto Item(string tag) =>
        new(Guid.NewGuid(), tag, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 6), PlanningPriority.Medium, [], null, null, [], Tag: tag);

    [Fact]
    public void A_plan_an_item_carries_by_slug_is_not_waiting_whatever_its_case_or_separators()
    {
        var plan = new RoadmapPlanDto([Item("release-q4")], [], []);

        var waiting = RoadmapShelf.Unplanned([Imported("Release_Q4"), Imported("release-q5")], plan);

        Assert.Equal(["release-q5"], waiting.Select(candidate => candidate.Tag));
    }

    [Theory]
    [InlineData("release_q4", "release-q4", true)]
    [InlineData("Café Plan", "cafe-plan", true)]
    [InlineData("release-q4", "release-q5", false)]
    public void Two_tags_are_the_same_plan_when_their_slugs_are(string tag, string other, bool same) =>
        Assert.Equal(same, RoadmapShelf.SameTag(tag, other));

    [Theory]
    [InlineData("release-q4", "Release q4")]
    [InlineData("docs_refresh", "Docs refresh")]
    [InlineData("-", "-")]
    public void The_title_is_read_off_the_tag(string tag, string title) =>
        Assert.Equal(title, RoadmapShelf.TitleOf(tag));
}
