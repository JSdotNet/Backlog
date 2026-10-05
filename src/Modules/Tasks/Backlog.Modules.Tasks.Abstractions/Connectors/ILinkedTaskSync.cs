namespace Backlog.Modules.Tasks.Abstractions.Connectors;

/// <summary>
/// The linked-task sync as a screen reaches it: run it now, without waiting for
/// the timer. The connector settings page's "Sync now" is the caller; the sync
/// itself stays in the Tasks module, which no screen references.
/// </summary>
public interface ILinkedTaskSync
{
    /// <summary>Syncs every enabled target whose connector is installed, now.
    /// Never throws: a target that fails is logged and the rest still run.</summary>
    Task RequestSync();
}
