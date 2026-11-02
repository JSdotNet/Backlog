using Backlog.Modules.Progress.Abstractions;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// <see cref="GoalOrder"/>: the one rule every reader lists goals by — the desktop's store,
/// the module and the phone. A goal placed by hand sorts by its rank; one never placed by
/// when it was set, so goals nobody moved keep the order they were set in; one that says
/// neither sorts last; the id breaks every tie, so every device lists them alike.
/// </summary>
public sealed class GoalOrderTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private sealed record Row(string Name, Guid Id, long? Rank, DateTimeOffset? CreatedAt);

    private static List<string> Sorted(params Row[] rows) =>
        [.. GoalOrder.Sort(rows, row => row.Rank, row => row.CreatedAt, row => row.Id).Select(row => row.Name)];

    [Fact]
    public void A_goal_never_placed_is_keyed_by_when_it_was_set_and_one_placed_by_its_rank()
    {
        Assert.Equal(Monday.UtcTicks, GoalOrder.Key(null, Monday));
        Assert.Equal(42, GoalOrder.Key(42, Monday));
        Assert.Equal(long.MaxValue, GoalOrder.Key(null, null));
    }

    [Fact]
    public void Goals_nobody_moved_keep_the_order_they_were_set_in()
    {
        var first = new Row("first", Guid.NewGuid(), null, Monday);
        var second = new Row("second", Guid.NewGuid(), null, Monday.AddMinutes(1));
        var third = new Row("third", Guid.NewGuid(), null, Monday.AddMinutes(2));

        Assert.Equal(["first", "second", "third"], Sorted(third, first, second));
    }

    [Fact]
    public void A_rank_places_a_goal_among_the_creation_keys_of_the_others()
    {
        var first = new Row("first", Guid.NewGuid(), null, Monday);
        var second = new Row("second", Guid.NewGuid(), null, Monday.AddMinutes(2));
        var moved = new Row("moved", Guid.NewGuid(), Monday.AddMinutes(1).UtcTicks, Monday.AddDays(3));

        Assert.Equal(["first", "moved", "second"], Sorted(second, moved, first));
    }

    [Fact]
    public void A_goal_that_says_neither_is_last_and_the_id_breaks_ties()
    {
        var low = new Guid("00000000-0000-0000-0000-000000000001");
        var high = new Guid("00000000-0000-0000-0000-000000000002");

        Assert.Equal(
            ["set", "tie-low", "tie-high", "unknown"],
            Sorted(
                new Row("unknown", Guid.NewGuid(), null, null),
                new Row("tie-high", high, 500, null),
                new Row("tie-low", low, 500, null),
                new Row("set", Guid.NewGuid(), 10, null)));
    }
}
