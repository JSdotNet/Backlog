using Backlog.SharedKernel.Changes;

namespace Backlog.SharedKernel.UnitTests.Changes;

/// <summary>
/// The ambient change set of local ADR 0061 §2: what each entry point's scope makes
/// current, what it puts back, and what work started under it inherits.
/// </summary>
public sealed class ChangeContextTests
{
    [Fact]
    public void A_scope_makes_its_change_set_current_and_puts_back_the_one_before()
    {
        Assert.Null(ChangeContext.Current);

        using (ChangeContext.Begin(ChangeActor.Person, "outer"))
        {
            var outer = ChangeContext.Current;
            Assert.Equal(ChangeActor.Person, outer?.Actor);

            using (ChangeContext.Begin(ChangeActor.System, "inner"))
            {
                Assert.Equal(ChangeActor.System, ChangeContext.Current?.Actor);
                Assert.NotEqual(outer!.Id, ChangeContext.Current!.Id);
            }

            Assert.Same(outer, ChangeContext.Current);
        }

        Assert.Null(ChangeContext.Current);
    }

    [Fact]
    public void Begin_if_none_joins_the_change_set_already_current()
    {
        using var gesture = ChangeContext.Begin(ChangeActor.Person, "bulk edit");
        var current = ChangeContext.Current;

        using (ChangeContext.BeginIfNone(ChangeActor.Person, "one row"))
        {
            Assert.Same(current, ChangeContext.Current);
        }

        Assert.Same(current, ChangeContext.Current);
    }

    [Fact]
    public void Begin_if_none_opens_one_when_nothing_is_current()
    {
        using (ChangeContext.BeginIfNone(ChangeActor.Person, "one row"))
        {
            Assert.Equal("one row", ChangeContext.Current?.Label);
        }

        Assert.Null(ChangeContext.Current);
    }

    [Fact]
    public async Task A_scope_opened_inside_an_async_method_ends_with_it()
    {
        await WriteAsync();

        Assert.Null(ChangeContext.Current);

        static async Task WriteAsync()
        {
            using var change = ChangeContext.Begin(ChangeActor.Sync, "page");
            await Task.Yield();
            Assert.Equal(ChangeActor.Sync, ChangeContext.Current?.Actor);
        }
    }

    [Fact]
    public async Task Work_started_while_detached_carries_no_change_set()
    {
        using var save = ChangeContext.Begin(ChangeActor.Person, "save");

        Task<ChangeSet?> inherited = Task.Run(() => ChangeContext.Current);

        Task<ChangeSet?> detached;
        using (ChangeContext.Detach())
        {
            detached = Task.Run(() => ChangeContext.Current);
        }

        Assert.NotNull(await inherited);
        Assert.Null(await detached);
        Assert.False(ExecutionContext.IsFlowSuppressed());
    }

    [Fact]
    public void Detaching_twice_leaves_the_outer_suppression_in_place()
    {
        using (ChangeContext.Detach())
        {
            using (ChangeContext.Detach())
            {
                Assert.True(ExecutionContext.IsFlowSuppressed());
            }

            Assert.True(ExecutionContext.IsFlowSuppressed());
        }

        Assert.False(ExecutionContext.IsFlowSuppressed());
    }

    [Theory]
    [InlineData(ChangeActor.Person, "person")]
    [InlineData(ChangeActor.Agent, "agent")]
    [InlineData(ChangeActor.Sync, "sync")]
    [InlineData(ChangeActor.System, "system")]
    [InlineData(ChangeActor.Unknown, "unknown")]
    public void The_actor_round_trips_through_its_stored_name(ChangeActor actor, string wire)
    {
        Assert.Equal(wire, ChangeSet.ToWire(actor));
        Assert.Equal(actor, ChangeSet.FromWire(wire));
    }
}
