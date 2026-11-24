using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// Editing a date, and reading the band colours a plan written before the choice moved
/// to Settings still carries. The date is the plan's to accept or refuse; the colours
/// are only ever read now, so what is asserted here is that an old file still parses
/// and that a value outside the sanctioned set is dropped rather than clamped.
/// </summary>
public class PlanMilestoneAndColourTests
{
    private static readonly DateOnly March = new(2026, 3, 31);

    private static (RoadmapPlan Plan, Milestone Milestone) Planned()
    {
        var plan = RoadmapPlan.Empty();
        var milestone = Assert.IsType<Ok<Milestone>>(plan.AddMilestone(
            "1.0",
            March,
            MilestoneKind.Release,
            RepositoryScope.Of(["backlog"])).Value).Value;

        return (plan, milestone);
    }

    [Fact]
    public void AMilestoneIsNotPlanWideUnlessItSaysSo()
    {
        var (_, milestone) = Planned();

        Assert.False(milestone.IsPlanWide);
    }

    [Fact]
    public void ADateCanBeReadAgainstTheWholePlan()
    {
        var plan = RoadmapPlan.Empty();

        var milestone = Assert.IsType<Ok<Milestone>>(plan.AddMilestone("Freeze", March, MilestoneKind.Freeze, isPlanWide: true).Value).Value;

        Assert.True(milestone.IsPlanWide);
    }

    [Fact]
    public void EveryFieldOfADateIsWrittenBack()
    {
        var (plan, milestone) = Planned();

        var updated = plan.UpdateMilestone(
            milestone.Id,
            "  1.1  ",
            new DateOnly(2026, 4, 30),
            MilestoneKind.Commitment,
            RepositoryScope.Of(["fincent"]),
            PlanningLane.Of("release"),
            isPlanWide: true);

        Assert.IsType<Ok<Milestone>>(updated.Value);
        Assert.Equal("1.1", milestone.Title);
        Assert.Equal(new DateOnly(2026, 4, 30), milestone.On);
        Assert.Equal(MilestoneKind.Commitment, milestone.Kind);
        Assert.Equal(["fincent"], milestone.Scope.Aliases);
        Assert.Equal("release", milestone.Lane.Name);
        Assert.True(milestone.IsPlanWide);
    }

    [Fact]
    public void ADateEditKeepsItsIdSoDependenciesOnItSurvive()
    {
        var plan = RoadmapPlan.Empty();
        var release = Assert.IsType<Ok<Milestone>>(plan.AddMilestone("1.0", March).Value).Value;
        var work = Assert.IsType<Ok<RoadmapItem>>(plan.AddItem(
            "Work",
            PlannedWindow.Of(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9))).Value).Value;
        plan.AddDependency(work.Id, release.Id);

        plan.UpdateMilestone(release.Id, "1.0.1", March.AddDays(7), MilestoneKind.Release);

        Assert.Equal([release.Id], work.Dependencies.All);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ADateWithoutANameIsRefused(string title)
    {
        var (plan, milestone) = Planned();

        var updated = plan.UpdateMilestone(milestone.Id, title, March, MilestoneKind.Release);

        Assert.IsType<Error>(updated.Value);
        Assert.Equal("roadmap.title_required", Assert.IsType<Error>(updated.Value).Code);
        Assert.Equal("1.0", milestone.Title);
    }

    [Fact]
    public void EditingADateThatIsNotThereIsRefused()
    {
        var (plan, _) = Planned();

        var updated = plan.UpdateMilestone(Guid.NewGuid(), "Anything", March, MilestoneKind.Release);

        Assert.IsType<Error>(updated.Value);
        Assert.Equal("roadmap.node_not_found", Assert.IsType<Error>(updated.Value).Code);
    }

    // --- Band colours ---------------------------------------------------------

    [Fact]
    public void NoBandHasAColourUntilOneIsChosen()
    {
        var plan = RoadmapPlan.Empty();

        Assert.Equal(0, plan.BandColours.Count);
        Assert.Null(plan.BandColours.For("backlog"));
    }

    [Fact]
    public void AStoredAliasIsNormalizedTheWayTheRestOfThePlanNormalizesOne()
    {
        var colours = BandColours.Of([new KeyValuePair<string, int>("  BACKLOG  ", 2)]);

        Assert.Equal(2, colours.For("backlog"));
    }

    [Fact]
    public void AStoredColourOutsideTheSetIsDroppedOnLoad_NotClamped()
    {
        // Clamping would hand somebody a hue they never chose and make it look like a
        // choice they had made.
        var colours = BandColours.Of([
            new KeyValuePair<string, int>("backlog", 9),
            new KeyValuePair<string, int>("fincent", 2)
        ]);

        Assert.Null(colours.For("backlog"));
        Assert.Equal(2, colours.For("fincent"));
    }

    [Fact]
    public void ColoursSurviveRehydration()
    {
        var plan = RoadmapPlan.Rehydrate(
            [],
            [],
            BandColours.Of([new KeyValuePair<string, int>("backlog", 3)]));

        Assert.Equal(3, plan.BandColours.For("backlog"));
    }
}
