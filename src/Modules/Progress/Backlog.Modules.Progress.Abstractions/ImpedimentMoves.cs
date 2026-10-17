namespace Backlog.Modules.Progress.Abstractions;

/// <summary>
/// The state order of an impediment (<c>.devbook/domain/progress/domain.md#impediment-state</c>):
/// forward along New, Reported, Escalated, Being handled, Escalated the one state a move may
/// skip, and any open state to Resolved; nothing leaves Resolved. Published so a state picker
/// offers only the moves <see cref="Services.IImpediments.MoveAsync"/> accepts; the aggregate
/// enforces the same rule through this helper.
/// </summary>
public static class ImpedimentMoves
{
    private static readonly ImpedimentState[] Order =
    [
        ImpedimentState.New,
        ImpedimentState.Reported,
        ImpedimentState.Escalated,
        ImpedimentState.BeingHandled,
        ImpedimentState.Resolved,
    ];

    /// <summary>Whether an impediment in <paramref name="from"/> may move to <paramref name="to"/>.</summary>
    public static bool Allows(ImpedimentState from, ImpedimentState to) =>
        (from, to) switch
        {
            (ImpedimentState.Resolved, _) => false,
            (_, ImpedimentState.Resolved) => true,
            (ImpedimentState.New, ImpedimentState.Reported) => true,
            (ImpedimentState.Reported, ImpedimentState.Escalated) => true,
            (ImpedimentState.Reported, ImpedimentState.BeingHandled) => true,
            (ImpedimentState.Escalated, ImpedimentState.BeingHandled) => true,
            _ => false,
        };

    /// <summary>The states an impediment in <paramref name="state"/> may move to, in the
    /// state order; empty for Resolved.</summary>
    public static IReadOnlyList<ImpedimentState> From(ImpedimentState state) =>
        Array.FindAll(Order, to => Allows(state, to));
}
