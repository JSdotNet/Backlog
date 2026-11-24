using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// The dependency rules — the reason the plan is the aggregate root rather than the
/// item. Acyclicity cannot be checked from inside one item, so these are all
/// assertions about the plan.
/// </summary>
public class PlanDependencyTests
{
    private static PlannedWindow Window(int startDay, int endDay) =>
        Assert.IsType<Ok<PlannedWindow>>(PlannedWindow.Create(new DateOnly(2026, 1, startDay), new DateOnly(2026, 1, endDay)).Value).Value;

    private static RoadmapItem Add(RoadmapPlan plan, string title, int startDay = 5, int endDay = 9) =>
        Assert.IsType<Ok<RoadmapItem>>(plan.AddItem(title, Window(startDay, endDay)).Value).Value;

    [Fact]
    public void ADependencyIsRecordedOnTheWaitingSide()
    {
        var plan = RoadmapPlan.Empty();
        var design = Add(plan, "Design");
        var build = Add(plan, "Build", 12, 16);

        var added = plan.AddDependency(build.Id, design.Id);

        Assert.IsType<Ok>(added.Value);
        Assert.Contains(design.Id, build.Dependencies.All);
        Assert.Empty(design.Dependencies.All);
    }

    [Fact]
    public void DeclaringTheSameDependencyTwice_ChangesNothingAndSucceeds()
    {
        var plan = RoadmapPlan.Empty();
        var design = Add(plan, "Design");
        var build = Add(plan, "Build", 12, 16);
        plan.AddDependency(build.Id, design.Id);

        var again = plan.AddDependency(build.Id, design.Id);

        Assert.IsType<Ok>(again.Value);
        Assert.Equal(1, build.Dependencies.Count);
    }

    [Fact]
    public void NothingCanWaitForItself()
    {
        var plan = RoadmapPlan.Empty();
        var item = Add(plan, "Design");

        var added = plan.AddDependency(item.Id, item.Id);

        Assert.IsType<Error>(added.Value);
        Assert.Equal("roadmap.self_dependency", Assert.IsType<Error>(added.Value).Code);
        Assert.Empty(item.Dependencies.All);
    }

    [Fact]
    public void ADirectCycleIsRefused_AndThePlanIsLeftAsItWas()
    {
        var plan = RoadmapPlan.Empty();
        var design = Add(plan, "Design");
        var build = Add(plan, "Build", 12, 16);
        plan.AddDependency(build.Id, design.Id);

        var added = plan.AddDependency(design.Id, build.Id);

        Assert.IsType<Error>(added.Value);
        Assert.Equal("roadmap.cyclic_dependency", Assert.IsType<Error>(added.Value).Code);
        Assert.Equal(ErrorType.Conflict, Assert.IsType<Error>(added.Value).Type);
        Assert.Empty(design.Dependencies.All);
        Assert.Equal(1, build.Dependencies.Count);
    }

    [Fact]
    public void AnIndirectCycleIsRefused_HoweverManyStepsAway()
    {
        var plan = RoadmapPlan.Empty();
        var first = Add(plan, "First");
        var second = Add(plan, "Second", 12, 16);
        var third = Add(plan, "Third", 19, 23);
        var fourth = Add(plan, "Fourth", 26, 30);

        plan.AddDependency(second.Id, first.Id);
        plan.AddDependency(third.Id, second.Id);
        plan.AddDependency(fourth.Id, third.Id);

        // First would now wait for Fourth, which waits (through Third and Second)
        // for First.
        var added = plan.AddDependency(first.Id, fourth.Id);

        Assert.IsType<Error>(added.Value);
        Assert.Equal("roadmap.cyclic_dependency", Assert.IsType<Error>(added.Value).Code);
        Assert.Empty(first.Dependencies.All);
    }

    [Fact]
    public void ACycleThroughAMilestoneIsRefusedToo()
    {
        var plan = RoadmapPlan.Empty();
        var work = Add(plan, "Finish the work");
        var release = Assert.IsType<Ok<Milestone>>(plan.AddMilestone("1.0", new DateOnly(2026, 2, 1)).Value).Value;

        Assert.IsType<Ok>(plan.AddDependency(release.Id, work.Id).Value);

        var added = plan.AddDependency(work.Id, release.Id);

        Assert.IsType<Error>(added.Value);
        Assert.Equal("roadmap.cyclic_dependency", Assert.IsType<Error>(added.Value).Code);
    }

    [Fact]
    public void ADiamondIsNotACycle()
    {
        var plan = RoadmapPlan.Empty();
        var root = Add(plan, "Root");
        var left = Add(plan, "Left", 12, 16);
        var right = Add(plan, "Right", 12, 16);
        var join = Add(plan, "Join", 19, 23);

        Assert.IsType<Ok>(plan.AddDependency(left.Id, root.Id).Value);
        Assert.IsType<Ok>(plan.AddDependency(right.Id, root.Id).Value);
        Assert.IsType<Ok>(plan.AddDependency(join.Id, left.Id).Value);
        Assert.IsType<Ok>(plan.AddDependency(join.Id, right.Id).Value);

        Assert.Equal(2, join.Dependencies.Count);
    }

    [Fact]
    public void ADependencyOnSomethingNotInThePlan_IsRefused()
    {
        var plan = RoadmapPlan.Empty();
        var item = Add(plan, "Design");

        var added = plan.AddDependency(item.Id, Guid.NewGuid());

        Assert.IsType<Error>(added.Value);
        Assert.Equal("roadmap.node_not_found", Assert.IsType<Error>(added.Value).Code);
        Assert.Empty(item.Dependencies.All);
    }

    [Fact]
    public void RemovingADependencyThatWasNeverThere_Succeeds()
    {
        var plan = RoadmapPlan.Empty();
        var item = Add(plan, "Design");

        var removed = plan.RemoveDependency(item.Id, Guid.NewGuid());

        Assert.IsType<Ok>(removed.Value);
    }

    [Fact]
    public void OverlappingDatesAcrossADependency_AreReportedNotRefused()
    {
        var plan = RoadmapPlan.Empty();
        var design = Add(plan, "Design", 5, 16);
        var build = Add(plan, "Build", 12, 23);

        var added = plan.AddDependency(build.Id, design.Id);

        Assert.IsType<Ok>(added.Value);

        var contradiction = Assert.Single(plan.Contradictions());
        Assert.Equal(build.Id, contradiction.NodeId);
        Assert.Equal(design.Id, contradiction.DependsOnId);
        Assert.Contains("Build", contradiction.Reason);
        Assert.Contains("Design", contradiction.Reason);
    }

    [Fact]
    public void WorkThatStartsAfterItsDependencyEnds_IsNotAContradiction()
    {
        var plan = RoadmapPlan.Empty();
        var design = Add(plan, "Design", 5, 9);
        var build = Add(plan, "Build", 10, 16);

        plan.AddDependency(build.Id, design.Id);

        Assert.Empty(plan.Contradictions());
    }

    [Fact]
    public void WorkThatStartsTheDayItsDependencyEnds_IsAContradiction()
    {
        // On the same day is not sequenced: the second cannot start on a day the
        // first is still running. It is also the boundary the timeline draws its
        // doubling-back arrow at, so the report and the picture agree.
        var plan = RoadmapPlan.Empty();
        var design = Add(plan, "Design", 5, 9);
        var build = Add(plan, "Build", 9, 16);

        plan.AddDependency(build.Id, design.Id);

        Assert.Single(plan.Contradictions());
    }

    [Fact]
    public void ReschedulingDoesNotDragTheDependentWorkAlong()
    {
        var plan = RoadmapPlan.Empty();
        var design = Add(plan, "Design", 5, 9);
        var build = Add(plan, "Build", 12, 16);
        plan.AddDependency(build.Id, design.Id);

        plan.Reschedule(design.Id, Window(12, 20));

        Assert.Equal(new DateOnly(2026, 1, 12), build.Window.Start);
        Assert.Single(plan.Contradictions());
    }
}
