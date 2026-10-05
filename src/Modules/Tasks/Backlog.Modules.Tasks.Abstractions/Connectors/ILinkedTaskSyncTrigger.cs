namespace Backlog.Modules.Tasks.Abstractions.Connectors;

/// <summary>
/// Starts a linked task sync now, rather than when the timer next finds a target
/// due.
/// <para>
/// A port because the screens that offer "Sync now" see only this project: the
/// timer that does the work is in the Tasks module, which a <c>.UI</c> project
/// never references. A host without the sync registers nothing behind it, and a
/// screen then offers no button.
/// </para>
/// </summary>
public interface ILinkedTaskSyncTrigger
{
    /// <summary>
    /// Syncs every enabled connected target whose connector is registered, whether
    /// or not its interval has passed. Waits for a run already in flight, then runs.
    /// Completes when the run does and never throws: a target that fails is logged
    /// and the rest still run.
    /// </summary>
    Task RequestSync();
}
