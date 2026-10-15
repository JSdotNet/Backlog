using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Features.CompleteItem;
using Backlog.Modules.Roadmap.Services;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// Completing an item by hand: the day a person says work no task carries was done, kept
/// until they reopen it. The date is the item's own state, so these go through the plan and
/// the handlers and never through a view; whether gathered tasks outrank it is a reading
/// question, asked where the plan is drawn.
/// </summary>
public class CompleteItemTests
{
    private static DateOnly Day(int day) => new(2026, 3, day);

    private readonly InMemoryPlans _plans = new();

    /// <summary>Noon on 10 March 2026, the day the handlers read as today.</summary>
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

    private CompleteItemCommandHandler Completing() => new(_plans, new RoadmapPlanGate(), _clock);

    private Guid Added()
    {
        var added = _plans.Plan.AddItem("Write the launch post", PlannedWindow.Of(Day(2), Day(6)));
        Assert.True(added.IsSuccess);
        return added.Value.Id;
    }

    [Fact]
    public void AnItemIsNotCompletedUntilSomebodyCompletesIt() =>
        Assert.Null(_plans.Plan.Items.SingleOrDefault(item => item.Id == Added())?.CompletedOn);

    [Fact]
    public void CompleteItem_RecordsTheDay_AndLeavesTheWindowWhereItWasPlanned()
    {
        var id = Added();

        var completed = _plans.Plan.CompleteItem(id, Day(9));

        Assert.True(completed.IsSuccess);
        Assert.Equal(Day(9), completed.Value.CompletedOn);
        Assert.Equal(PlannedWindow.Of(Day(2), Day(6)), completed.Value.Window);
    }

    [Fact]
    public void CompleteItem_AgainOnAnotherDay_MovesTheDay()
    {
        var id = Added();
        _plans.Plan.CompleteItem(id, Day(9));

        var again = _plans.Plan.CompleteItem(id, Day(4));

        Assert.True(again.IsSuccess);
        Assert.Equal(Day(4), again.Value.CompletedOn);
    }

    [Fact]
    public void CompleteItem_RefusesAnUnknownItem() =>
        Assert.Equal("roadmap.item_not_found", _plans.Plan.CompleteItem(Guid.NewGuid(), Day(9)).Error.Code);

    [Fact]
    public void ReopenItem_ClearsTheDay()
    {
        var id = Added();
        _plans.Plan.CompleteItem(id, Day(9));

        var reopened = _plans.Plan.ReopenItem(id);

        Assert.True(reopened.IsSuccess);
        Assert.Null(reopened.Value.CompletedOn);
    }

    [Fact]
    public void ReopenItem_OnAnOpenItem_LeavesItOpen()
    {
        var id = Added();

        var reopened = _plans.Plan.ReopenItem(id);

        Assert.True(reopened.IsSuccess);
        Assert.Null(reopened.Value.CompletedOn);
    }

    /// <summary>Moving or editing a completed item keeps it completed: only Reopen takes the
    /// day away. A person who drags finished work or fixes its title has not said it is
    /// open again.</summary>
    [Theory]
    [InlineData("reschedule")]
    [InlineData("pin-end")]
    [InlineData("unpin-end")]
    [InlineData("update")]
    [InlineData("prioritise")]
    public void MovingOrEditingACompletedItem_KeepsItCompleted(string change)
    {
        var id = Added();
        _plans.Plan.CompleteItem(id, Day(9));

        var changed = change switch
        {
            "reschedule" => _plans.Plan.Reschedule(id, PlannedWindow.Of(Day(12), Day(16))),
            "pin-end" => _plans.Plan.PinEnd(id, Day(20)),
            "unpin-end" => _plans.Plan.UnpinEnd(id),
            "update" => _plans.Plan.UpdateItem(id, "Write the launch post, again", PlannedWindow.Of(Day(3), Day(8)), Abstractions.PlanningPriority.High),
            _ => _plans.Plan.Prioritise(id, Abstractions.PlanningPriority.Low)
        };

        Assert.True(changed.IsSuccess);
        Assert.Equal(Day(9), changed.Value.CompletedOn);
    }

    [Fact]
    public void ReopenItem_RefusesAnUnknownItem() =>
        Assert.Equal("roadmap.item_not_found", _plans.Plan.ReopenItem(Guid.NewGuid()).Error.Code);

    [Fact]
    public async Task TheCompleteCommand_StoresTheDay_AndReportsItOnTheDto()
    {
        var id = Added();

        var result = await Completing()
            .Handle(new CompleteItemCommand(id, Day(9)), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(Day(9), result.Value.CompletedOn);
        Assert.Equal(1, _plans.Saves);
    }

    [Fact]
    public async Task TheCompleteCommand_SavesNothing_ForAnUnknownItem()
    {
        var result = await Completing()
            .Handle(new CompleteItemCommand(Guid.NewGuid(), Day(9)), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(0, _plans.Saves);
    }

    [Fact]
    public async Task TheCompleteCommand_TakesToday()
    {
        var id = Added();

        var result = await Completing()
            .Handle(new CompleteItemCommand(id, Day(10)), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(Day(10), result.Value.CompletedOn);
    }

    [Fact]
    public async Task TheCompleteCommand_RefusesADayAfterToday_AndSavesNothing()
    {
        var id = Added();

        var result = await Completing()
            .Handle(new CompleteItemCommand(id, Day(11)), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("roadmap.completed_in_future", result.Error.Code);
        Assert.Equal(0, _plans.Saves);
        Assert.Null(_plans.Plan.Items.Single().CompletedOn);
    }

    [Fact]
    public async Task TheReopenCommand_StoresTheReopening()
    {
        var id = Added();
        _plans.Plan.CompleteItem(id, Day(9));

        var result = await new ReopenItemCommandHandler(_plans, new RoadmapPlanGate())
            .Handle(new ReopenItemCommand(id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.CompletedOn);
        Assert.Equal(1, _plans.Saves);
    }

    [Fact]
    public async Task TheReopenCommand_FailsForAnUnknownItem()
    {
        var result = await new ReopenItemCommandHandler(_plans, new RoadmapPlanGate())
            .Handle(new ReopenItemCommand(Guid.NewGuid()), TestContext.Current.CancellationToken);

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
