using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;

using Xunit;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// One table, in one place, with one caller left standing where it used to be.
/// <para>
/// The lifecycle graph moved off <see cref="TaskItem"/> and into
/// <see cref="EntryStatusFlow"/> so that the MCP <c>transition</c> tool could ask
/// it without referencing a module implementation (local ADR 0012 §5). A move
/// like that has exactly one way to go wrong quietly: the old statics keep
/// compiling while answering from a table somebody left behind, and every test
/// about either side keeps passing while the two drift apart.
/// </para>
/// <para>
/// So this asserts the agreement itself, over every pair of statuses there is
/// rather than over a chosen handful. <c>TaskItemLifecycleTests</c> and
/// <c>StatusReadingTests</c> — both unmodified by the move, which is the other
/// half of the proof — say what the graph's edges are; this says there is only
/// one graph to have them.
/// </para>
/// </summary>
public class EntryStatusFlowTests
{
    /// <summary>Every pair, both directions, including a status with itself. A
    /// theory with a row per edge would test the edges somebody remembered.</summary>
    [Fact]
    public void The_aggregates_predicate_and_the_published_graph_agree_on_every_pair()
    {
        foreach (var from in Enum.GetValues<EntryStatus>())
        {
            foreach (var to in Enum.GetValues<EntryStatus>())
            {
                Assert.Equal(
                    EntryStatusFlow.IsAllowed(from, to),
                    TaskItem.IsTransitionAllowed(from, to));
            }
        }
    }

    /// <summary>The same for the explanation a refusal carries. Order matters as
    /// well as membership: the list is shown to a person, and the first entry is
    /// the move the lifecycle expects next.</summary>
    [Fact]
    public void The_next_steps_are_the_same_list_in_the_same_order()
    {
        foreach (var from in Enum.GetValues<EntryStatus>())
        {
            Assert.Equal(EntryStatusFlow.NextFrom(from), TaskItem.NextStatusesFrom(from));
        }
    }

    /// <summary>
    /// The edges <c>.devbook/domain/tasks/flow.md#task-lifecycle</c> draws, stated here
    /// against the published graph rather than against the aggregate.
    /// <para>
    /// Pinned rather than derived from the other two tests: those say the two
    /// sides agree, and two sides that agree on the wrong graph agree just as
    /// well. This is the one assertion in the pair that would fail if the move
    /// had changed an edge.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(EntryStatus.Draft, new[] { EntryStatus.Ready })]
    [InlineData(EntryStatus.Ready, new[] { EntryStatus.InProgress, EntryStatus.Draft })]
    [InlineData(EntryStatus.InProgress, new[] { EntryStatus.Done, EntryStatus.Ready })]
    [InlineData(EntryStatus.Done, new[] { EntryStatus.Archived, EntryStatus.InProgress })]
    [InlineData(EntryStatus.Archived, new[] { EntryStatus.Draft })]
    public void The_graph_is_the_one_the_domain_folder_draws(EntryStatus from, EntryStatus[] next)
    {
        Assert.Equal(next, EntryStatusFlow.NextFrom(from));
    }

    /// <summary>A status may always be "moved" to itself, which is what makes a
    /// repeated request an answer rather than an error — and it is never listed
    /// as a next step, because staying put is not somewhere to go.</summary>
    [Fact]
    public void A_status_reaches_itself_without_being_a_next_step()
    {
        foreach (var status in Enum.GetValues<EntryStatus>())
        {
            Assert.True(EntryStatusFlow.IsAllowed(status, status));
            Assert.DoesNotContain(status, EntryStatusFlow.NextFrom(status));
        }
    }

    /// <summary>
    /// The answer cannot be cast back to the stored array and written through.
    /// <para>
    /// <see cref="IReadOnlyList{T}"/> is a promise about the interface, not about
    /// the object behind it, so returning the dictionary's own
    /// <see cref="EntryStatus"/><c>[]</c> would leave the lifecycle writable by
    /// anyone holding an answer. The consequence is not a tidiness one: a single
    /// assignment through that cast is process-wide and permanent, and the next
    /// caller to ask whether Ready may jump straight to Archived is told yes.
    /// </para>
    /// <para>
    /// Asserted here rather than trusted to the return type, because the type is
    /// exactly what makes the bug invisible at the call site.
    /// </para>
    /// </summary>
    [Fact]
    public void The_next_steps_cannot_be_written_through()
    {
        var next = EntryStatusFlow.NextFrom(EntryStatus.Ready);

        Assert.IsNotType<EntryStatus[]>(next);

        // And the graph still says what it said, which is the property the cast
        // would have broken.
        Assert.False(EntryStatusFlow.IsAllowed(EntryStatus.Ready, EntryStatus.Archived));
        Assert.Equal([EntryStatus.InProgress, EntryStatus.Draft], EntryStatusFlow.NextFrom(EntryStatus.Ready));
    }
}
