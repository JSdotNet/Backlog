using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Features.PinItemEnd;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// Pinning the end of started work: a person's date that outranks the forecast until
/// they hand it back. The pin is the item's own state, so these go through the plan
/// and the handlers and never through a view.
/// </summary>
public class PinItemEndTests
{
    private static DateOnly Day(int day) => new(2026, 3, day);

    private static PlannedWindow Window(int start, int end) => PlannedWindow.Of(Day(start), Day(end));

    private readonly InMemoryPlans _plans = new();

    private Guid Added(int start = 2, int end = 6)
    {
        var added = _plans.Plan.AddItem("Move devbook", Window(start, end));
        Assert.True(added.IsSuccess);
        return added.Value.Id;
    }

    [Fact]
    public void AnItemIsNotPinnedUntilSomebodyPinsIt() =>
        Assert.False(_plans.Plan.Items.Count > 0 && _plans.Plan.Items[0].EndPinned);

    [Fact]
    public void PinEnd_SetsTheEnd_AndMarksTheItemPinned()
    {
        var id = Added();

        var pinned = _plans.Plan.PinEnd(id, Day(20));

        Assert.True(pinned.IsSuccess);
        Assert.Equal(Day(20), pinned.Value.Window.End);
        Assert.Equal(Day(2), pinned.Value.Window.Start);
        Assert.True(pinned.Value.EndPinned);
    }

    [Fact]
    public void PinEnd_BeforeThePlannedStart_MovesTheStartBackToThePin()
    {
        // Started work is drawn from when it began, which can be before its planned
        // start, so a pin there is valid: the window becomes that one day.
        var id = Added();

        var pinned = _plans.Plan.PinEnd(id, Day(1));

        Assert.True(pinned.IsSuccess);
        Assert.True(pinned.Value.EndPinned);
        Assert.Equal(Window(1, 1), pinned.Value.Window);
    }

    [Fact]
    public void PinEnd_OnTheStart_KeepsTheStart()
    {
        var id = Added();

        var pinned = _plans.Plan.PinEnd(id, Day(2));

        Assert.Equal(Window(2, 2), pinned.Value.Window);
    }

    [Fact]
    public void PinEnd_ToTheDateTheImporterPlaced_StillHandsTheWindowToThePerson()
    {
        var plan = _plans.Plan;
        var imported = plan.AddImportedItem("By effort", PlanningTag.Of("by-effort"), Window(2, 6), ImportPlacement.Effort);

        plan.PinEnd(imported.Value.Id, Day(6));

        Assert.Null(imported.Value.PlacedByImport);
        Assert.True(imported.Value.EndPinned);
    }

    [Fact]
    public void PinEnd_RefusesAnUnknownItem() =>
        Assert.Equal("roadmap.item_not_found", _plans.Plan.PinEnd(Guid.NewGuid(), Day(9)).Error.Code);

    [Fact]
    public void PinEnd_HandsTheWindowToThePerson_SoAnImportKeepsIt()
    {
        var plan = _plans.Plan;
        var imported = plan.AddImportedItem("By effort", PlanningTag.Of("by-effort"), Window(2, 6), ImportPlacement.Effort);

        plan.PinEnd(imported.Value.Id, Day(12));

        Assert.Null(imported.Value.PlacedByImport);
    }

    [Fact]
    public void UnpinEnd_ClearsThePin_AndLeavesTheEndWhereItIs()
    {
        var id = Added();
        _plans.Plan.PinEnd(id, Day(20));

        var unpinned = _plans.Plan.UnpinEnd(id);

        Assert.True(unpinned.IsSuccess);
        Assert.False(unpinned.Value.EndPinned);
        Assert.Equal(Day(20), unpinned.Value.Window.End);
    }

    [Fact]
    public void UnpinEnd_RefusesAnUnknownItem() =>
        Assert.Equal("roadmap.item_not_found", _plans.Plan.UnpinEnd(Guid.NewGuid()).Error.Code);

    [Fact]
    public void MovingAPinnedItem_KeepsItPinned()
    {
        var id = Added();
        _plans.Plan.PinEnd(id, Day(20));

        var moved = _plans.Plan.Reschedule(id, Window(9, 25));

        Assert.True(moved.Value.EndPinned);
        Assert.Equal(Day(25), moved.Value.Window.End);
    }

    [Fact]
    public async Task ThePinCommand_StoresThePin_AndReportsItOnTheDto()
    {
        var id = Added();

        var result = await new PinItemEndCommandHandler(_plans)
            .Handle(new PinItemEndCommand(id, Day(18)), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.EndPinned);
        Assert.Equal(Day(18), result.Value.End);
        Assert.Equal(1, _plans.Saves);
    }

    [Fact]
    public async Task ThePinCommand_SavesNothing_ForAnUnknownItem()
    {
        var result = await new PinItemEndCommandHandler(_plans)
            .Handle(new PinItemEndCommand(Guid.NewGuid(), Day(1)), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(0, _plans.Saves);
    }

    [Fact]
    public async Task TheUnpinCommand_StoresTheRelease()
    {
        var id = Added();
        _plans.Plan.PinEnd(id, Day(18));

        var result = await new UnpinItemEndCommandHandler(_plans)
            .Handle(new UnpinItemEndCommand(id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.EndPinned);
        Assert.Equal(1, _plans.Saves);
    }

    [Fact]
    public async Task TheUnpinCommand_FailsForAnUnknownItem()
    {
        var result = await new UnpinItemEndCommandHandler(_plans)
            .Handle(new UnpinItemEndCommand(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(0, _plans.Saves);
    }

    private sealed class InMemoryPlans : IRoadmapPlanRepository
    {
        public RoadmapPlan Plan { get; private set; } = RoadmapPlan.Empty();

        public int Saves { get; private set; }

        public Task<RoadmapPlan> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Plan);

        public Task SaveAsync(RoadmapPlan plan, CancellationToken cancellationToken = default)
        {
            Plan = plan;
            Saves++;
            return Task.CompletedTask;
        }
    }
}
