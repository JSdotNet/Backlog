using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Modules.Tasks.UnitTests.LinkedTasks;

/// <summary>
/// The sync decides whether to touch a linked task by comparing the reference it
/// would write with the one held, so equality is load-bearing: every value counts,
/// and the flags count as a set.
/// </summary>
public sealed class SourceRefTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Two_references_with_the_same_values_are_equal_whatever_order_the_flags_came_in()
    {
        var first = Reference(flags: [LinkedTaskFlags.Vanished, LinkedTaskFlags.DoneLocally]);
        var second = Reference(flags: [LinkedTaskFlags.DoneLocally, LinkedTaskFlags.Vanished, LinkedTaskFlags.Vanished]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal([LinkedTaskFlags.DoneLocally, LinkedTaskFlags.Vanished], second.Flags);
    }

    [Fact]
    public void A_different_assignee_state_stamp_or_flag_is_a_different_reference()
    {
        var reference = Reference();

        Assert.NotEqual(reference, reference with { Assignee = "Job" });
        Assert.NotEqual(reference, reference with { SourceState = "closed" });
        Assert.NotEqual(reference, reference with { SourceUpdatedAt = Stamp.AddSeconds(1) });
        Assert.NotEqual(reference, reference with { NormalisedState = NormalisedSourceState.Done });
        Assert.NotEqual(reference, reference with { SourceTitle = "Renamed at the source" });
        Assert.NotEqual(reference, reference.WithFlag(LinkedTaskFlags.Vanished, set: true));
    }

    [Fact]
    public void Setting_a_flag_already_set_answers_the_same_reference()
    {
        var reference = Reference(flags: [LinkedTaskFlags.Vanished]);

        Assert.Same(reference, reference.WithFlag(LinkedTaskFlags.Vanished, set: true));
        Assert.Empty(reference.WithFlag(LinkedTaskFlags.Vanished, set: false).Flags);
    }

    [Fact]
    public void A_blank_assignee_is_no_assignee()
    {
        Assert.Null(Reference(assignee: "  ").Assignee);
    }

    private static SourceRef Reference(string? assignee = null, IEnumerable<string>? flags = null) =>
        new("github", "JSdotNet/Backlog", "I_1", "https://github.com/JSdotNet/Backlog/issues/1", "#1", assignee, "open", Stamp, flags);
}
