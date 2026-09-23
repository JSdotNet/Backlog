using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.DomainModels;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// <c>placed_by_import</c> on the aggregate (ADR 0013, ruling 5): set by an import,
/// cleared the moment a person moves the window, and never overruled after that.
/// </summary>
public class PlanImportProvenanceTests
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private static (RoadmapPlan Plan, RoadmapItem Item) Imported()
    {
        var plan = RoadmapPlan.Empty();
        var item = plan.AddImportedItem(
            "Imported", PlanningTag.Of("imported"), PlannedWindow.Of(Monday, Monday.AddDays(4)), ImportPlacement.Effort).Value;
        return (plan, item);
    }

    [Fact]
    public void AnImportedItem_CarriesItsTagAndTheRuleThatPlacedIt()
    {
        var (_, item) = Imported();

        Assert.Equal("imported", item.Tag.Value);
        Assert.Equal(ImportPlacement.Effort, item.PlacedByImport);
    }

    [Fact]
    public void AnItemAddedByHand_IsNotPlacedByImport()
    {
        var plan = RoadmapPlan.Empty();

        var item = plan.AddItem("By hand", PlannedWindow.Of(Monday, Monday)).Value;

        Assert.Null(item.PlacedByImport);
    }

    [Fact]
    public void Rescheduling_ClearsIt()
    {
        var (plan, item) = Imported();

        plan.Reschedule(item.Id, PlannedWindow.Of(Monday.AddDays(7), Monday.AddDays(11)));

        Assert.Null(item.PlacedByImport);
    }

    [Fact]
    public void ALaneOnlyDrop_KeepsIt_BecauseNoDateWasOverruled()
    {
        var (plan, item) = Imported();

        plan.Reschedule(item.Id, item.Window, PlanningLane.Of("platform"));

        Assert.Equal(ImportPlacement.Effort, item.PlacedByImport);
    }

    [Fact]
    public void TheImporterMayReplaceItsOwnWindow()
    {
        var (plan, item) = Imported();
        var due = PlannedWindow.Of(Monday, Monday.AddDays(20));

        var placed = plan.PlaceByImport(item.Id, due, ImportPlacement.DueDate);

        Assert.True(placed.IsSuccess);
        Assert.Equal(due, item.Window);
        Assert.Equal(ImportPlacement.DueDate, item.PlacedByImport);
    }

    [Fact]
    public void TheImporterMayNotMoveAWindowAPersonPlaced()
    {
        var (plan, item) = Imported();
        var chosen = PlannedWindow.Of(Monday.AddDays(7), Monday.AddDays(11));
        plan.Reschedule(item.Id, chosen);

        var placed = plan.PlaceByImport(item.Id, PlannedWindow.Of(Monday, Monday), ImportPlacement.Effort);

        Assert.True(placed.IsFailure);
        Assert.Equal("roadmap.placed_by_hand", placed.Error.Code);
        Assert.Equal(chosen, item.Window);
    }

    [Fact]
    public void ClearingDependencies_EmptiesTheSet_SoAReimportReplacesRatherThanMerges()
    {
        var plan = RoadmapPlan.Empty();
        var a = plan.AddItem("A", PlannedWindow.Of(Monday, Monday)).Value;
        var b = plan.AddItem("B", PlannedWindow.Of(Monday, Monday)).Value;
        plan.AddDependency(b.Id, a.Id);

        Assert.True(plan.ClearDependencies(b.Id).IsSuccess);

        Assert.Empty(b.Dependencies.All);
    }
}
