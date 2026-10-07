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

    /// <summary>
    /// Syncs the one target named by the pair, now, whether or not its interval
    /// has passed, and answers how it went — what a target's own "Sync now" says
    /// back to the person. Waits for a run already in flight, then runs.
    /// <para>
    /// Never throws: a sync that failed, a target that is not connected or is
    /// switched off, and a sync that has stopped are each an outcome with a
    /// sentence, so the screen never says "Synced." about something that was not.
    /// </para>
    /// </summary>
    Task<LinkedTaskSyncOutcome> RequestSync(string connectorId, string target);
}

/// <summary>How one target's on-demand sync went.</summary>
/// <param name="Succeeded">Whether the sync ran to the end.</param>
/// <param name="Message">What the person reads: what the sync did, or why it did
/// not, naming the target either way.</param>
/// <param name="ErrorCode">The failure's stable code, such as
/// <c>linked_tasks.fetch_not_found</c>, or null when it succeeded.</param>
public sealed record LinkedTaskSyncOutcome(bool Succeeded, string Message, string? ErrorCode = null);
