using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Features.PutPlanOnMyDay;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// A whole plan on My Day (local ADR 0062 §8): a My Day date on the plan's Roadmap Items,
/// kept until the person takes it off. Whether the plan is in a given day's My Day is a
/// reading question Tasks answers; these cover the stamp the plan keeps and the item a
/// plan tag with no Roadmap Item gets.
/// </summary>
public class PutPlanOnMyDayTests
{
    private static DateOnly Day(int day) => new(2026, 11, day);

    private readonly InMemoryPlans _plans = new();

    private PutPlanOnMyDayCommandHandler Putting() => new(_plans, new RoadmapPlanGate());

    private RoadmapItem Added(string title, string tag)
    {
        var added = _plans.Plan.AddItem(title, PlannedWindow.Of(Day(2), Day(20)), tag: PlanningTag.Of(tag));
        return Assert.IsType<Ok<RoadmapItem>>(added.Value).Value;
    }

    [Fact]
    public void An_item_is_on_no_My_Day_until_somebody_puts_its_plan_there() =>
        Assert.Null(Added("Zero trust auth", "zero-trust-auth").MyDayOn);

    [Fact]
    public void Putting_a_plan_on_My_Day_stamps_its_item_and_leaves_the_window_where_it_was_planned()
    {
        var item = Added("Zero trust auth", "zero-trust-auth");

        var put = _plans.Plan.PutPlanOnMyDay(PlanningTag.Of("zero-trust-auth"), Day(24));

        var stamped = Assert.Single(Assert.IsType<Ok<IReadOnlyList<RoadmapItem>>>(put.Value).Value);
        Assert.Same(item, stamped);
        Assert.Equal(Day(24), item.MyDayOn);
        Assert.Equal(PlannedWindow.Of(Day(2), Day(20)), item.Window);
    }

    [Fact]
    public void Putting_it_on_another_day_moves_the_stamp_and_taking_it_off_clears_it()
    {
        var item = Added("Zero trust auth", "zero-trust-auth");
        _plans.Plan.PutPlanOnMyDay(PlanningTag.Of("zero-trust-auth"), Day(24));

        _plans.Plan.PutPlanOnMyDay(PlanningTag.Of("zero-trust-auth"), Day(25));
        Assert.Equal(Day(25), item.MyDayOn);

        _plans.Plan.PutPlanOnMyDay(PlanningTag.Of("zero-trust-auth"), null);
        Assert.Null(item.MyDayOn);
    }

    [Fact]
    public void A_shared_tag_is_one_plan_so_every_item_carrying_it_takes_the_date()
    {
        var first = Added("Auth: devices", "zero-trust-auth");
        var second = Added("Auth: agents", "zero-trust-auth");
        var other = Added("Finance setup", "finance-setup");

        _plans.Plan.PutPlanOnMyDay(PlanningTag.Of("zero-trust-auth"), Day(24));

        Assert.Equal(Day(24), first.MyDayOn);
        Assert.Equal(Day(24), second.MyDayOn);
        Assert.Null(other.MyDayOn);
    }

    [Fact]
    public void A_plan_tag_no_item_carries_gets_its_item_on_that_day()
    {
        Added("Finance setup", "finance-setup");

        var put = _plans.Plan.PutPlanOnMyDay(PlanningTag.Of("lets-bbq-setup"), Day(24));

        var created = Assert.Single(Assert.IsType<Ok<IReadOnlyList<RoadmapItem>>>(put.Value).Value);
        Assert.Equal(2, _plans.Plan.Items.Count);
        Assert.Equal("lets-bbq-setup", created.Tag.Value);
        Assert.Equal("lets-bbq-setup", created.Title);
        Assert.Equal(PlannedWindow.Of(Day(24), Day(24)), created.Window);
        Assert.Equal(Day(24), created.MyDayOn);
        Assert.True(created.Scope.Aliases.Count == 0);
    }

    [Fact]
    public void Taking_a_plan_tag_no_item_carries_off_My_Day_creates_nothing()
    {
        var put = _plans.Plan.PutPlanOnMyDay(PlanningTag.Of("lets-bbq-setup"), null);

        Assert.Empty(Assert.IsType<Ok<IReadOnlyList<RoadmapItem>>>(put.Value).Value);
        Assert.Empty(_plans.Plan.Items);
    }

    [Fact]
    public async Task The_handler_reads_the_tag_with_or_without_its_sigil_and_saves_the_stamp()
    {
        Added("Zero trust auth", "zero-trust-auth");

        var put = await Putting().Handle(new PutPlanOnMyDayCommand("+zero-trust-auth", Day(24)), TestContext.Current.CancellationToken);

        var item = Assert.Single(Assert.IsType<Ok<IReadOnlyList<RoadmapItemDto>>>(put.Value).Value);
        Assert.Equal(Day(24), item.MyDayOn);
        Assert.Equal(1, _plans.Saves);
    }

    [Fact]
    public async Task The_handler_creates_the_item_for_a_new_plan_tag_and_saves_it()
    {
        var put = await Putting().Handle(new PutPlanOnMyDayCommand("+lets-bbq-setup", Day(24)), TestContext.Current.CancellationToken);

        var item = Assert.Single(Assert.IsType<Ok<IReadOnlyList<RoadmapItemDto>>>(put.Value).Value);
        Assert.Equal("lets-bbq-setup", item.Tag);
        Assert.Equal(Day(24), item.MyDayOn);
        Assert.Equal(1, _plans.Saves);
        Assert.Single(_plans.Plan.Items);
    }

    [Fact]
    public async Task The_handler_writes_nothing_when_a_plan_no_item_carries_is_taken_off()
    {
        var put = await Putting().Handle(new PutPlanOnMyDayCommand("lets-bbq-setup", null), TestContext.Current.CancellationToken);

        Assert.Empty(Assert.IsType<Ok<IReadOnlyList<RoadmapItemDto>>>(put.Value).Value);
        Assert.Equal(0, _plans.Saves);
    }

    [Fact]
    public async Task The_handler_writes_nothing_when_the_plan_already_has_that_day()
    {
        Added("Zero trust auth", "zero-trust-auth");
        await Putting().Handle(new PutPlanOnMyDayCommand("zero-trust-auth", Day(24)), TestContext.Current.CancellationToken);

        await Putting().Handle(new PutPlanOnMyDayCommand("zero-trust-auth", Day(24)), TestContext.Current.CancellationToken);
        await Putting().Handle(new PutPlanOnMyDayCommand("finance-setup", null), TestContext.Current.CancellationToken);

        Assert.Equal(1, _plans.Saves);
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
