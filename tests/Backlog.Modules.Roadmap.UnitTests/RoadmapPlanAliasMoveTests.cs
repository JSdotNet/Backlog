using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// A relabelled repository's roadmap data follows it (local ADR 0060 §3): the band colour
/// it was given and every item and milestone filed under it move to the new alias. The
/// plan travels as one document (local ADR 0018), so the move is a save the replica
/// pushes like any other edit.
/// </summary>
public class RoadmapPlanAliasMoveTests
{
    private static DateOnly Day(int day) => new(2026, 3, day);

    private static Dictionary<string, string> Map(params (string From, string To)[] pairs) =>
        pairs.ToDictionary(pair => pair.From, pair => pair.To, StringComparer.Ordinal);

    private static RoadmapPlan PlanColoured(params (string Alias, int Colour)[] colours) =>
        RoadmapPlan.Rehydrate(
            [],
            [],
            BandColours.Of(colours.Select(c => new KeyValuePair<string, int>(c.Alias, c.Colour))));

    private static RoadmapItem ItemUnder(RoadmapPlan plan, params string[] aliases) =>
        plan.AddItem("Ship the sync", PlannedWindow.Of(Day(2), Day(6)), scope: RepositoryScope.Of(aliases)).Value;

    private static Milestone MilestoneUnder(RoadmapPlan plan, params string[] aliases) =>
        plan.AddMilestone("Release", Day(9), scope: RepositoryScope.Of(aliases)).Value;

    // The plan

    [Fact]
    public void A_moved_alias_takes_its_band_colour_with_it()
    {
        var plan = PlanColoured(("project-guidelines-mcp", 3), ("backlog", 1));

        var changed = plan.MoveRepositoryAliases(Map(("project-guidelines-mcp", "app-defaults")));

        Assert.True(changed);
        Assert.Equal(3, plan.BandColours.For("app-defaults"));
        Assert.Null(plan.BandColours.For("project-guidelines-mcp"));
        Assert.Equal(1, plan.BandColours.For("backlog"));
    }

    [Fact]
    public void Where_the_new_alias_already_has_a_colour_its_own_wins()
    {
        // A merge: the kept repository's data wins (ADR 0060 §1).
        var plan = PlanColoured(("old-api", 2), ("api", 4));

        plan.MoveRepositoryAliases(Map(("old-api", "api")));

        Assert.Equal(4, plan.BandColours.For("api"));
        Assert.Null(plan.BandColours.For("old-api"));
        Assert.Equal(1, plan.BandColours.Count);
    }

    [Fact]
    public void A_swap_exchanges_two_band_colours_rather_than_running_one_move_into_the_other()
    {
        var plan = PlanColoured(("front", 1), ("back", 5));

        plan.MoveRepositoryAliases(Map(("front", "back"), ("back", "front")));

        Assert.Equal(5, plan.BandColours.For("front"));
        Assert.Equal(1, plan.BandColours.For("back"));
    }

    [Fact]
    public void An_item_and_a_milestone_filed_under_a_moved_alias_are_filed_under_its_new_one()
    {
        var plan = RoadmapPlan.Empty();
        var item = ItemUnder(plan, "project-guidelines-mcp", "backlog");
        var milestone = MilestoneUnder(plan, "project-guidelines-mcp");

        var changed = plan.MoveRepositoryAliases(Map(("project-guidelines-mcp", "app-defaults")));

        Assert.True(changed);
        Assert.Equal(["app-defaults", "backlog"], item.Scope.Aliases);
        Assert.Equal(["app-defaults"], milestone.Scope.Aliases);
    }

    [Fact]
    public void A_scope_naming_both_the_old_and_the_new_alias_collapses_to_one()
    {
        // Scope aliases are normalized and distinct (roadmap invariant).
        var plan = RoadmapPlan.Empty();
        var item = ItemUnder(plan, "old-api", "api");

        plan.MoveRepositoryAliases(Map(("old-api", "api")));

        Assert.Equal(["api"], item.Scope.Aliases);
    }

    [Fact]
    public void A_swap_exchanges_the_aliases_items_are_filed_under()
    {
        var plan = RoadmapPlan.Empty();
        var front = ItemUnder(plan, "front");
        var back = MilestoneUnder(plan, "back");

        plan.MoveRepositoryAliases(Map(("front", "back"), ("back", "front")));

        Assert.Equal(["back"], front.Scope.Aliases);
        Assert.Equal(["front"], back.Scope.Aliases);
    }

    [Fact]
    public void An_alias_the_mapping_does_not_name_is_kept_and_nothing_changes()
    {
        // An unresolved scope alias is kept (roadmap invariant): only moved aliases move.
        var plan = PlanColoured(("gone-repo", 2));
        var item = ItemUnder(plan, "gone-repo");
        var unfiled = plan.AddItem("Think", PlannedWindow.Of(Day(2), Day(3))).Value;

        var changed = plan.MoveRepositoryAliases(Map(("other", "elsewhere")));

        Assert.False(changed);
        Assert.Equal(["gone-repo"], item.Scope.Aliases);
        Assert.True(unfiled.Scope.IsUnfiled);
        Assert.Equal(2, plan.BandColours.For("gone-repo"));
    }

    [Fact]
    public void The_mapping_is_read_the_way_the_plan_normalizes_an_alias()
    {
        var plan = PlanColoured(("backlog", 3));
        var item = ItemUnder(plan, "backlog");

        plan.MoveRepositoryAliases(Map(("  Backlog ", "BL")));

        Assert.Equal(3, plan.BandColours.For("bl"));
        Assert.Equal(["bl"], item.Scope.Aliases);
    }

    // The store the follower drives

    [Fact]
    public void The_plan_is_a_replicated_store_holding_every_alias_it_colours_or_files_under()
    {
        var plans = new InMemoryPlans(PlanColoured(("coloured", 1)));
        ItemUnder(plans.Plan, "item-repo", "shared");
        MilestoneUnder(plans.Plan, "milestone-repo", "shared");
        plans.Plan.AddItem("Unfiled", PlannedWindow.Of(Day(2), Day(3)));

        IRepositoryAliasKeyedStore store = Store(plans, new RoadmapPlanChanges());

        Assert.Equal(AliasKeyedStoreKind.Replicated, store.Kind);
        Assert.Equal(
            ["coloured", "item-repo", "milestone-repo", "shared"],
            store.HeldAliases().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_move_saves_the_plan_once_and_the_replica_hears_it_as_a_local_change()
    {
        var plans = new InMemoryPlans(PlanColoured(("project-guidelines-mcp", 3)));
        ItemUnder(plans.Plan, "project-guidelines-mcp");
        var changes = new RoadmapPlanChanges();
        var replication = new RoadmapReplication([], changes);
        var heard = 0;
        replication.Changed += () => heard++;

        IRepositoryAliasKeyedStore store = Store(plans, changes);
        var changed = await store.MoveAliasesAsync(
            Map(("project-guidelines-mcp", "app-defaults")),
            TestContext.Current.CancellationToken);

        Assert.True(changed);
        Assert.Equal(1, plans.Saves);
        Assert.Equal(1, heard);
        Assert.Equal(3, plans.Plan.BandColours.For("app-defaults"));
        Assert.Equal(["app-defaults"], plans.Plan.Items.Single().Scope.Aliases);
    }

    [Fact]
    public async Task A_mapping_that_names_no_held_alias_neither_saves_nor_announces()
    {
        var plans = new InMemoryPlans(PlanColoured(("backlog", 3)));
        var changes = new RoadmapPlanChanges();
        var heard = 0;
        changes.Changed += () => heard++;

        IRepositoryAliasKeyedStore store = Store(plans, changes);
        var changed = await store.MoveAliasesAsync(Map(("other", "elsewhere")), TestContext.Current.CancellationToken);

        Assert.False(changed);
        Assert.Equal(0, plans.Saves);
        Assert.Equal(0, heard);
    }

    [Fact]
    public async Task A_second_call_with_the_same_mapping_changes_nothing()
    {
        var plans = new InMemoryPlans(PlanColoured(("backlog", 3)));
        IRepositoryAliasKeyedStore store = Store(plans, new RoadmapPlanChanges());
        var mapping = Map(("backlog", "bl"));

        await store.MoveAliasesAsync(mapping, TestContext.Current.CancellationToken);
        var again = await store.MoveAliasesAsync(mapping, TestContext.Current.CancellationToken);

        Assert.False(again);
        Assert.Equal(1, plans.Saves);
    }

    [Fact]
    public async Task An_edit_saves_the_moved_plan_as_a_new_version()
    {
        var plans = new InMemoryPlans(PlanColoured(("backlog", 3)));
        IRepositoryAliasKeyedStore store = Store(plans, new RoadmapPlanChanges());

        var changed = await store.MoveAliasesAsync(
            Map(("backlog", "bl")),
            AliasMoveOrigin.Edit,
            TestContext.Current.CancellationToken);

        Assert.True(changed);
        Assert.Equal(1, plans.Saves);
        Assert.Equal(0, plans.SavesKeepingStamp);
    }

    [Fact]
    public async Task A_backstop_move_keeps_the_plan_stamp_but_still_stores_and_announces_it()
    {
        // Every device's backstop re-keys its own copy; stamped now, a stale copy would
        // outrank the editing device's re-keyed plan and lose its edits (ADR 0018, LWW).
        var plans = new InMemoryPlans(PlanColoured(("backlog", 3)));
        ItemUnder(plans.Plan, "backlog");
        var changes = new RoadmapPlanChanges();
        var heard = 0;
        changes.Changed += () => heard++;
        IRepositoryAliasKeyedStore store = Store(plans, changes);

        var changed = await store.MoveAliasesAsync(
            Map(("backlog", "bl")),
            AliasMoveOrigin.Backstop,
            TestContext.Current.CancellationToken);

        Assert.True(changed);
        Assert.Equal(0, plans.Saves);
        Assert.Equal(1, plans.SavesKeepingStamp);
        Assert.Equal(1, heard);
        Assert.Equal(3, plans.Plan.BandColours.For("bl"));
        Assert.Equal(["bl"], plans.Plan.Items.Single().Scope.Aliases);
    }

    [Fact]
    public void Two_aliases_moved_onto_one_uncoloured_alias_give_it_the_colour_of_the_first_in_ordinal_order()
    {
        // Inserted in the other order, so enumeration order cannot be what decides.
        var plan = PlanColoured(("zeta", 5), ("alpha", 2));

        plan.MoveRepositoryAliases(Map(("zeta", "merged"), ("alpha", "merged")));

        Assert.Equal(2, plan.BandColours.For("merged"));
        Assert.Equal(1, plan.BandColours.Count);
    }

    private static RoadmapAliasKeyedStore Store(InMemoryPlans plans, RoadmapPlanChanges changes) =>
        new(plans, new RoadmapPlanGate(), changes);

    private sealed class InMemoryPlans(RoadmapPlan plan) : IRoadmapPlanRepository
    {
        public RoadmapPlan Plan { get; private set; } = plan;

        public int Saves { get; private set; }

        public int SavesKeepingStamp { get; private set; }

        public Task<RoadmapPlan> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Plan);

        public Task SaveAsync(RoadmapPlan plan, CancellationToken cancellationToken = default)
        {
            Plan = plan;
            Saves++;
            return Task.CompletedTask;
        }

        public Task SaveKeepingStampAsync(RoadmapPlan plan, CancellationToken cancellationToken = default)
        {
            Plan = plan;
            SavesKeepingStamp++;
            return Task.CompletedTask;
        }
    }
}
