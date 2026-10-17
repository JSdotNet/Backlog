using Backlog.Modules.Progress.Abstractions;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// The state order of <c>.devbook/domain/progress/domain.md#impediment-state</c> as the
/// published <see cref="ImpedimentMoves"/> helper answers it, for a picker that offers only
/// the moves an impediment accepts.
/// </summary>
public sealed class ImpedimentMovesTests
{
    [Theory]
    [InlineData(ImpedimentState.New, ImpedimentState.Reported)]
    [InlineData(ImpedimentState.Reported, ImpedimentState.Escalated)]
    [InlineData(ImpedimentState.Reported, ImpedimentState.BeingHandled)]
    [InlineData(ImpedimentState.Escalated, ImpedimentState.BeingHandled)]
    [InlineData(ImpedimentState.New, ImpedimentState.Resolved)]
    [InlineData(ImpedimentState.Reported, ImpedimentState.Resolved)]
    [InlineData(ImpedimentState.Escalated, ImpedimentState.Resolved)]
    [InlineData(ImpedimentState.BeingHandled, ImpedimentState.Resolved)]
    public void A_forward_move_or_any_open_state_to_Resolved_is_allowed(ImpedimentState from, ImpedimentState to) =>
        Assert.True(ImpedimentMoves.Allows(from, to));

    [Theory]
    [InlineData(ImpedimentState.New, ImpedimentState.New)]
    [InlineData(ImpedimentState.New, ImpedimentState.Escalated)]
    [InlineData(ImpedimentState.New, ImpedimentState.BeingHandled)]
    [InlineData(ImpedimentState.Reported, ImpedimentState.New)]
    [InlineData(ImpedimentState.Reported, ImpedimentState.Reported)]
    [InlineData(ImpedimentState.Escalated, ImpedimentState.Reported)]
    [InlineData(ImpedimentState.BeingHandled, ImpedimentState.Escalated)]
    [InlineData(ImpedimentState.BeingHandled, ImpedimentState.BeingHandled)]
    [InlineData(ImpedimentState.Resolved, ImpedimentState.Resolved)]
    [InlineData(ImpedimentState.Resolved, ImpedimentState.New)]
    [InlineData(ImpedimentState.Resolved, ImpedimentState.Reported)]
    public void A_backward_or_skipping_move_or_any_move_from_Resolved_is_refused(ImpedimentState from, ImpedimentState to) =>
        Assert.False(ImpedimentMoves.Allows(from, to));

    [Fact]
    public void From_answers_each_states_legal_moves_in_the_state_order()
    {
        Assert.Equal([ImpedimentState.Reported, ImpedimentState.Resolved], ImpedimentMoves.From(ImpedimentState.New));
        Assert.Equal(
            [ImpedimentState.Escalated, ImpedimentState.BeingHandled, ImpedimentState.Resolved],
            ImpedimentMoves.From(ImpedimentState.Reported));
        Assert.Equal([ImpedimentState.BeingHandled, ImpedimentState.Resolved], ImpedimentMoves.From(ImpedimentState.Escalated));
        Assert.Equal([ImpedimentState.Resolved], ImpedimentMoves.From(ImpedimentState.BeingHandled));
        Assert.Empty(ImpedimentMoves.From(ImpedimentState.Resolved));
    }
}
