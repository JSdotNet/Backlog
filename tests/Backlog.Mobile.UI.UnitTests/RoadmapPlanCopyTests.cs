using Backlog.Mobile.UI.Tasks;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The roadmap plan document on the phone (local ADR 0062 §8): folded off the task feed by
/// its own stamps, kept whole, and read for one thing — which plans are on My Day and for
/// which day.
/// </summary>
public sealed class RoadmapPlanCopyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The id the desktop writes the plan under — persisted, so a literal here.</summary>
    private static readonly Guid PlanId = Guid.Parse("7c1a0b52-3d4e-4f6a-9b8c-0d1e2f3a4b01");

    [Fact]
    public void The_plans_on_My_Day_are_read_off_the_items_in_their_order_by_slug()
    {
        var copy = new RoadmapPlanCopy(Now, 1, """
            {"version":1,"items":[
              {"id":"a","title":"BBQ","tag":"Lets_BBQ","myDayOn":"2026-11-25"},
              {"id":"b","title":"No day","tag":"quiet"},
              {"id":"c","title":"No tag","myDayOn":"2026-11-25"},
              {"id":"d","title":"Docs","tag":"docs","myDayOn":"2026-11-24"}
            ]}
            """);

        Assert.Equal(
            [("lets-bbq", new DateOnly(2026, 11, 25)), ("docs", new DateOnly(2026, 11, 24))],
            copy.PlansOnMyDay);
    }

    [Fact]
    public void Items_sharing_a_tag_that_disagree_read_the_latest_day()
    {
        var copy = new RoadmapPlanCopy(Now, 1, """
            {"items":[{"tag":"docs","myDayOn":"2026-11-20"},{"tag":"docs","myDayOn":"2026-11-25"},{"tag":"docs","myDayOn":"2026-11-22"}]}
            """);

        Assert.Equal([("docs", new DateOnly(2026, 11, 25))], copy.PlansOnMyDay);
    }

    [Theory]
    [InlineData("""{"items":[{"tag":"docs","myDayOn":"25-11-2026"}]}""")]
    [InlineData("""{"items":[{"tag":7,"myDayOn":"2026-11-25"}]}""")]
    [InlineData("""{"items":"none"}""")]
    [InlineData("""{}""")]
    public void An_item_or_list_this_build_cannot_read_puts_nothing_on_My_Day(string content)
    {
        Assert.Empty(new RoadmapPlanCopy(Now, 1, content).PlansOnMyDay);
    }

    [Fact]
    public void The_later_stamp_wins_whichever_record_brings_it()
    {
        var later = Record(Now.AddMinutes(2), 2, """{"items":[{"tag":"later","myDayOn":"2026-11-25"}]}""");
        var earlier = Record(Now.AddMinutes(1), 3, """{"items":[]}""");

        var folded = RoadmapPlanCopy.Fold(null, [later, earlier]);

        Assert.NotNull(folded);
        Assert.Equal(Now.AddMinutes(2), folded.UpdatedAt);
        Assert.Null(RoadmapPlanCopy.Fold(folded, [earlier]));
    }

    [Fact]
    public void An_equal_stamp_goes_to_the_higher_server_stamp()
    {
        var kept = new RoadmapPlanCopy(Now, 5, """{"items":[]}""");

        Assert.Null(RoadmapPlanCopy.Fold(kept, [Record(Now, 4, """{"items":[{"tag":"x"}]}""")]));
        Assert.Equal(6, RoadmapPlanCopy.Fold(kept, [Record(Now, 6, """{"items":[]}""")])!.ServerTimestamp);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1]")]
    public void A_copy_this_build_cannot_read_leaves_the_one_kept(string content)
    {
        var kept = new RoadmapPlanCopy(Now, 1, """{"items":[]}""");

        Assert.Null(RoadmapPlanCopy.Fold(kept, [Record(Now.AddMinutes(1), 2, content)]));
    }

    [Fact]
    public void Other_kinds_are_not_a_plan_and_the_pace_is_not_kept()
    {
        var task = new TaskChangeRecord(TestTasks.Task("A task", Now), Guid.Empty, 1);
        var pace = new TaskChangeRecord(
            TestTasks.Task("Planning pace", Now, type: "planning-pace", contentMd: """{"items":[]}"""), Guid.Empty, 2);

        Assert.Null(RoadmapPlanCopy.Fold(null, [task, pace]));
    }

    private static TaskChangeRecord Record(DateTimeOffset updatedAt, long serverTimestamp, string content) =>
        new(TestTasks.Task("Roadmap plan", updatedAt, type: "roadmap-plan", id: PlanId, contentMd: content), Guid.Empty, serverTimestamp);
}
