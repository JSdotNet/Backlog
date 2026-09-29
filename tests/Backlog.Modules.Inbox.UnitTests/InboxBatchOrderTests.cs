using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// The order a batch goes in and the loops that stop it: every item after what
/// it waits on and otherwise as asked, and each loop named by titles the way
/// the panel and the refusal both say it.
/// </summary>
public sealed class InboxBatchOrderTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-7000-8000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-7000-8000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-7000-8000-00000000000c");
    private static readonly Guid D = Guid.Parse("00000000-0000-7000-8000-00000000000d");

    private static readonly Dictionary<Guid, string> Titles = new() { [A] = "A", [B] = "B", [C] = "C", [D] = "D" };

    private static InboxBatchEdge After(Guid from, Guid to) => new(from, to);

    [Fact]
    public void Without_dependencies_the_order_is_the_order_asked()
    {
        Assert.Equal([C, A, B], InboxBatchOrder.Order([C, A, B], []));
    }

    [Fact]
    public void Every_item_goes_after_what_it_waits_on_and_otherwise_stays_where_it_was_asked()
    {
        // A waits on C, C waits on D; B waits on nothing.
        var order = InboxBatchOrder.Order([A, B, C, D], [After(A, C), After(C, D)]);

        Assert.Equal([B, D, C, A], order);
    }

    [Fact]
    public void An_edge_to_an_item_outside_the_batch_or_to_itself_is_no_dependency()
    {
        var stranger = Guid.CreateVersion7();

        Assert.Equal([A, B], InboxBatchOrder.Order([A, B], [After(A, stranger), After(B, B)]));
        Assert.Empty(InboxBatchOrder.Loops([A, B], [After(A, stranger), After(B, B)]));
    }

    [Fact]
    public void Items_in_a_loop_still_all_go_in_the_order_asked()
    {
        var order = InboxBatchOrder.Order([A, B, C], [After(A, B), After(B, A)]);

        Assert.Equal([C, A, B], order);
    }

    [Fact]
    public void A_loop_of_two_is_named_from_the_item_asked_first_back_to_itself()
    {
        var loops = InboxBatchOrder.Loops([A, B, C], [After(B, A), After(A, B)]);

        var loop = Assert.Single(loops);
        Assert.Equal([A, B], loop);
        Assert.Equal("A → B → A", InboxBatchOrder.Name(loop, id => Titles[id]));
    }

    [Fact]
    public void A_longer_loop_is_named_in_the_direction_of_waiting()
    {
        // A waits on B, B on C, C on A; D waits on A but is not in the loop.
        var loops = InboxBatchOrder.Loops([A, B, C, D], [After(A, B), After(B, C), After(C, A), After(D, A)]);

        Assert.Equal("A → B → C → A", InboxBatchOrder.Name(Assert.Single(loops), id => Titles[id]));
    }

    [Fact]
    public void Separate_loops_are_each_named_in_the_order_they_start()
    {
        var loops = InboxBatchOrder.Loops([A, B, C, D], [After(C, D), After(D, C), After(A, B), After(B, A)]);

        Assert.Equal(["A → B → A", "C → D → C"], loops.Select(loop => InboxBatchOrder.Name(loop, id => Titles[id])));
    }

    [Fact]
    public void Turning_one_edge_of_a_loop_off_leaves_no_loop()
    {
        Assert.Empty(InboxBatchOrder.Loops([A, B], [After(A, B)]));
    }

    [Fact]
    public void Only_dependencies_on_items_are_edges()
    {
        var task = new DependencyTarget(DependencyTargetKind.Task, C, "A task", "c-import");
        var dependencies = new[]
        {
            new ProposedDependency(A, DependencyTarget.ForItem(B, "B"), "B"),
            new ProposedDependency(A, task, "#12"),
        };

        Assert.Equal([After(A, B)], InboxBatchOrder.Edges(dependencies));
    }
}
