namespace Backlog.Modules.Progress.Abstractions;

/// <summary>
/// Which moves an impediment's state may make (<c>.devbook/domain/progress/domain.md#impediment-state</c>),
/// in the module's published surface so the domain refuses and the phone offers by the one
/// rule (local ADR 0035 §8): forward along New, Reported, Escalated, Being handled — Escalated
/// the one state a move may skip — and any open state to Resolved. A resolved impediment
/// moves nowhere.
/// </summary>
public static class ImpedimentStates
{
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
}
