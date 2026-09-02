using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.DomainModels;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// The plan's own rules. These test the aggregate directly rather than through a
/// handler, because the aggregate is where every one of them lives — a handler that
/// happened to enforce the same rule would prove nothing about the next caller.
/// </summary>
public class RoadmapPlanTests
{
    private static readonly DateOnly January = new(2026, 1, 5);

    private static PlannedWindow Window(int startDay, int endDay) =>
        PlannedWindow.Create(new DateOnly(2026, 1, startDay), new DateOnly(2026, 1, endDay)).Value;

    private static RoadmapItem Add(RoadmapPlan plan, string title, int startDay = 5, int endDay = 9) =>
        plan.AddItem(title, Window(startDay, endDay)).Value;

    [Fact]
    public void EmptyPlan_IsAValidPlan()
    {
        var plan = RoadmapPlan.Empty();

        Assert.True(plan.IsEmpty);
        Assert.Empty(plan.Items);
        Assert.Empty(plan.Milestones);
        Assert.Empty(plan.Contradictions());
    }

    [Fact]
    public void AddedItem_KeepsWhatItWasGiven_AndDefaultsTheRest()
    {
        var plan = RoadmapPlan.Empty();

        var item = plan.AddItem("Extract the sync service", Window(5, 9)).Value;

        Assert.Equal("Extract the sync service", item.Title);
        Assert.Equal(new DateOnly(2026, 1, 5), item.Window.Start);
        Assert.Equal(new DateOnly(2026, 1, 9), item.Window.End);
        Assert.Equal(PlanningPriority.Medium, item.Priority);
        Assert.True(item.Scope.IsUnfiled);
        Assert.True(item.Lane.IsDefault);
        Assert.Null(item.TaskId);
        Assert.Equal(0, item.Dependencies.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ItemWithoutATitle_IsRefused(string title)
    {
        var plan = RoadmapPlan.Empty();

        var added = plan.AddItem(title, Window(5, 9));

        Assert.True(added.IsFailure);
        Assert.Equal("roadmap.title_required", added.Error.Code);
        Assert.Empty(plan.Items);
    }

    [Fact]
    public void TitleIsTrimmed_BecauseALeadingSpaceIsNotPartOfTheName()
    {
        var plan = RoadmapPlan.Empty();

        var item = plan.AddItem("  Ship 1.0  ", Window(5, 9)).Value;

        Assert.Equal("Ship 1.0", item.Title);
    }

    [Fact]
    public void RescheduleMovesTheDates_AndLeavesTheLaneAloneWhenNoneIsGiven()
    {
        var plan = RoadmapPlan.Empty();
        var item = plan.AddItem("Migrate storage", Window(5, 9), lane: PlanningLane.Of("platform")).Value;

        var rescheduled = plan.Reschedule(item.Id, Window(12, 23));

        Assert.True(rescheduled.IsSuccess);
        Assert.Equal(new DateOnly(2026, 1, 12), item.Window.Start);
        Assert.Equal(new DateOnly(2026, 1, 23), item.Window.End);
        Assert.Equal("platform", item.Lane.Name);
    }

    [Fact]
    public void RescheduleOntoAnotherLane_RefilesIt()
    {
        var plan = RoadmapPlan.Empty();
        var item = plan.AddItem("Migrate storage", Window(5, 9), lane: PlanningLane.Of("platform")).Value;

        plan.Reschedule(item.Id, Window(5, 9), PlanningLane.Of("migration"));

        Assert.Equal("migration", item.Lane.Name);
    }

    [Fact]
    public void ReschedulingSomethingThatIsNotThere_IsRefused()
    {
        var plan = RoadmapPlan.Empty();

        var rescheduled = plan.Reschedule(Guid.NewGuid(), Window(5, 9));

        Assert.True(rescheduled.IsFailure);
        Assert.Equal("roadmap.item_not_found", rescheduled.Error.Code);
    }

    [Fact]
    public void PlanningPriority_IsThePlansOwn_AndDoesNotTouchTheLinkedEntry()
    {
        var entryId = Guid.NewGuid();
        var plan = RoadmapPlan.Empty();
        var item = plan.AddItem("Refine the backlog", Window(5, 9), taskId: entryId).Value;

        plan.Prioritise(item.Id, PlanningPriority.Critical);

        Assert.Equal(PlanningPriority.Critical, item.Priority);
        // The link is still just an id afterwards. There is nothing in this module
        // that could have written to the entry, and this asserts that the link did
        // not quietly become something more than an id.
        Assert.Equal(entryId, item.TaskId);
    }

    [Fact]
    public void RemovingAnItem_AlsoForgetsEveryDependencyOnIt()
    {
        var plan = RoadmapPlan.Empty();
        var first = Add(plan, "Design");
        var second = Add(plan, "Build", 12, 16);
        plan.AddDependency(second.Id, first.Id);

        plan.RemoveItem(first.Id);

        Assert.DoesNotContain(first.Id, second.Dependencies.All);
        Assert.Single(plan.Items);
    }

    [Fact]
    public void RemovingAMilestone_AlsoForgetsEveryDependencyOnIt()
    {
        var plan = RoadmapPlan.Empty();
        var freeze = plan.AddMilestone("Code freeze", January, MilestoneKind.Freeze).Value;
        var item = Add(plan, "Release notes", 12, 16);
        plan.AddDependency(item.Id, freeze.Id);

        plan.RemoveMilestone(freeze.Id);

        Assert.DoesNotContain(freeze.Id, item.Dependencies.All);
        Assert.Empty(plan.Milestones);
    }

    [Fact]
    public void AMilestoneIsADay_NotAOneDayItem()
    {
        var plan = RoadmapPlan.Empty();

        var milestone = plan.AddMilestone("1.0", January).Value;

        Assert.Equal(January, milestone.On);
        Assert.Equal(MilestoneKind.Release, milestone.Kind);
        // It is not in Items, and there is no window to ask for its length.
        Assert.Empty(plan.Items);
        Assert.Single(plan.Milestones);
    }
}
