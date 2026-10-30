using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.DomainModels;
using Backlog.Modules.Progress.Services;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// <see cref="GoalOrdering.MoveTo"/>: putting a goal at a place in the person's order. One
/// goal is written per move — it takes a rank between its new neighbours — so last-write-wins
/// per goal document never sets two goals against each other. Only when its neighbours have
/// no room left between them are the goals around the move given new ranks as well.
/// </summary>
public sealed class GoalOrderingTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Goals set a minute apart, in the order given.</summary>
    private static List<Goal> Set(params string[] titles) =>
        [.. titles.Select((title, index) => Goal.Create(Guid.NewGuid(), title, null, null, Monday.AddMinutes(index)).Value)];

    private static List<string> Order(IEnumerable<Goal> goals) =>
        [.. GoalOrder.Sort(goals, goal => goal.Rank, goal => goal.CreatedAt, goal => goal.Id).Select(goal => goal.Title)];

    [Theory]
    [InlineData("c", 0, new[] { "c", "a", "b", "d" })]
    [InlineData("a", 2, new[] { "b", "c", "a", "d" })]
    [InlineData("b", 3, new[] { "a", "c", "d", "b" })]
    [InlineData("d", 1, new[] { "a", "d", "b", "c" })]
    public void A_goal_moved_to_a_place_takes_it_and_only_that_goal_is_written(string moved, int to, string[] expected)
    {
        var goals = Set("a", "b", "c", "d");
        var goal = goals.Single(g => g.Title == moved);

        var written = GoalOrdering.MoveTo(goals, goal, to);

        Assert.Equal(expected, Order(goals));
        Assert.Same(goal, Assert.Single(written));
        Assert.NotNull(goal.Rank);
    }

    [Fact]
    public void Between_two_neighbours_the_rank_is_their_midpoint()
    {
        var goals = Set("a", "b", "c");

        GoalOrdering.MoveTo(goals, goals[2], 1);

        var a = goals[0].OrderKey;
        var b = goals[1].OrderKey;
        Assert.Equal(a + (b - a) / 2, goals[2].Rank);
    }

    [Fact]
    public void At_the_top_and_the_bottom_the_goal_is_a_gap_beyond_the_end()
    {
        var goals = Set("a", "b", "c");

        GoalOrdering.MoveTo(goals, goals[2], 0);
        Assert.Equal(goals[0].OrderKey - GoalOrder.Gap, goals[2].Rank);

        GoalOrdering.MoveTo(goals, goals[0], 2);
        Assert.Equal(goals[1].OrderKey + GoalOrder.Gap, goals[0].Rank);
    }

    [Theory]
    [InlineData(-5, new[] { "c", "a", "b" })]
    [InlineData(99, new[] { "a", "b", "c" })]
    public void A_place_past_either_end_is_the_end(int to, string[] expected)
    {
        var goals = Set("a", "b", "c");

        GoalOrdering.MoveTo(goals, goals[2], to);

        Assert.Equal(expected, Order(goals));
    }

    [Fact]
    public void A_goal_moved_to_the_place_it_holds_writes_nothing()
    {
        var goals = Set("a", "b", "c");

        Assert.Empty(GoalOrdering.MoveTo(goals, goals[1], 1));
        Assert.Null(goals[1].Rank);
    }

    /// <summary>Moving into the same gap again and again halves it until nothing is left;
    /// then the goals around the move are spread out again, and they are written too.</summary>
    [Fact]
    public void When_the_neighbours_have_no_room_left_the_goals_around_the_move_are_ranked_again()
    {
        var a = Goal.Restore(Guid.NewGuid(), "a", null, null, [], Monday, rank: 100);
        var b = Goal.Restore(Guid.NewGuid(), "b", null, null, [], Monday, rank: 101);
        var c = Goal.Restore(Guid.NewGuid(), "c", null, null, [], Monday, rank: 102);
        var far = Goal.Restore(Guid.NewGuid(), "far", null, null, [], Monday, rank: 10_000);
        List<Goal> goals = [a, b, c, far];

        var written = GoalOrdering.MoveTo(goals, c, 1);

        Assert.Equal(["a", "c", "b", "far"], Order(goals));
        Assert.Contains(c, written);
        Assert.True(written.Count > 1);
        Assert.DoesNotContain(far, written);
        Assert.Equal(10_000, far.Rank);
    }

    [Fact]
    public void Moves_into_one_gap_keep_the_order_right_however_many_there_are()
    {
        var goals = Set("a", "b", "c", "d");
        var expected = new List<string> { "a", "b", "c", "d" };

        for (var round = 0; round < 80; round++)
        {
            // The last goal goes to the second place, every time: always the gap after "a".
            var last = goals.Single(g => g.Title == Order(goals)[^1]);
            GoalOrdering.MoveTo(goals, last, 1);

            var title = expected[^1];
            expected.RemoveAt(expected.Count - 1);
            expected.Insert(1, title);
            Assert.Equal(expected, Order(goals));
        }
    }

    // --- A new goal lands last -----------------------------------------------------

    private static Goal SetAt(string title, DateTimeOffset at) => Goal.Create(Guid.NewGuid(), title, null, null, at).Value;

    [Fact]
    public void A_new_goal_set_after_every_key_is_last_already_and_is_left_unranked()
    {
        var goals = Set("a", "b");
        var fresh = SetAt("fresh", Monday.AddMinutes(5));
        goals.Add(fresh);

        GoalOrdering.PlaceNew(goals, fresh);

        Assert.Null(fresh.Rank);
        Assert.Equal(["a", "b", "fresh"], Order(goals));
    }

    /// <summary>A move to the bottom ranks the goal a gap past the newest key — ahead of the
    /// clock — so a goal set inside that gap is ranked past it in turn.</summary>
    [Fact]
    public void A_new_goal_lands_last_after_a_move_to_the_bottom()
    {
        var goals = Set("a", "b", "c");
        GoalOrdering.MoveTo(goals, goals[0], 2);
        var fresh = SetAt("fresh", Monday.AddMinutes(3));
        goals.Add(fresh);

        GoalOrdering.PlaceNew(goals, fresh);

        Assert.Equal(["b", "c", "a", "fresh"], Order(goals));
        Assert.True(fresh.Rank > goals[0].Rank);
    }

    /// <summary>The re-rank that spreads the goals from a key before the move to the end of the
    /// list runs ahead of the clock as well; a new goal still lands below them.</summary>
    [Fact]
    public void A_new_goal_lands_last_after_the_goals_at_the_bottom_were_ranked_again()
    {
        var d = Goal.Restore(Guid.NewGuid(), "d", null, null, [], Monday, rank: Monday.AddHours(-1).UtcTicks);
        var a = Goal.Restore(Guid.NewGuid(), "a", null, null, [], Monday, rank: Monday.UtcTicks);
        var b = Goal.Restore(Guid.NewGuid(), "b", null, null, [], Monday, rank: Monday.UtcTicks + 1);
        var c = Goal.Restore(Guid.NewGuid(), "c", null, null, [], Monday, rank: Monday.UtcTicks + 2);
        List<Goal> goals = [d, a, b, c];

        GoalOrdering.MoveTo(goals, a, 2);
        Assert.Equal(["d", "b", "a", "c"], Order(goals));
        Assert.True(c.OrderKey > Monday.AddMinutes(1).UtcTicks);

        var fresh = SetAt("fresh", Monday.AddMinutes(1));
        goals.Add(fresh);
        GoalOrdering.PlaceNew(goals, fresh);

        Assert.Equal(["d", "b", "a", "c", "fresh"], Order(goals));
    }

    [Fact]
    public void A_goal_set_without_a_date_can_still_be_placed_after_it()
    {
        var undated = Goal.Restore(Guid.NewGuid(), "undated", null, null, [], createdAt: null);
        var goals = Set("a", "b");
        goals.Add(undated);

        GoalOrdering.MoveTo(goals, goals[0], 2);

        Assert.Equal(["b", "undated", "a"], Order(goals));
    }
}
