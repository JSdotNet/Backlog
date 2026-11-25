namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// The stage each plan entry's session has reached, as the plan session reports it to the
/// delivery surface (local ADR 0062 §2: <b>Now</b> lists a plan's entries in a session
/// "each with the session and its stage"). Answered by an adapter over the Sessions
/// context's delivery runs, so this module never references Sessions; a host that composes
/// no run source registers one that answers with nothing, and an entry in a session reads
/// off its own status with no stage.
/// </summary>
public interface IPlanEntryStages
{
    /// <summary>Raised, on whatever thread wrote it, when a run may have moved a stage;
    /// a listener reads again through <see cref="ReadAsync"/>.</summary>
    event Action? Changed;

    /// <summary>The stage label of each entry a run under way or parked is working, by the
    /// entry's stored id, such as <c>Review · 8 of 9</c>. An entry no run reports is absent.
    /// Never throws for an unreadable source: what could not be read is not in the answer.</summary>
    Task<IReadOnlyDictionary<Guid, string>> ReadAsync(CancellationToken cancellationToken = default);
}
