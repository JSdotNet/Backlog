using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Abstractions.Connectors;

/// <summary>
/// The linked tasks a disconnected source left behind, as the connector settings
/// page reaches them: which sources still have tasks on the list, and deleting
/// one source's tasks in one step.
/// <para>
/// Disconnecting a target keeps its tasks (<see cref="IConnectedTargets.Remove"/>),
/// so this is how a person clears them when they no longer want them. Deleting
/// tombstones the tasks here and writes nothing back to the source.
/// </para>
/// </summary>
public interface IDisconnectedLinkedTasks
{
    /// <summary>Every connector-and-target pair that still has a task on the list
    /// and no connected target, ordered by connector id and then target.</summary>
    Task<IReadOnlyList<DisconnectedSource>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tombstones every task the pair brought in, whatever its status, and answers
    /// how many. Refused, with nothing deleted, when the pair is connected again by
    /// the time this runs: its tasks are the sync's again.
    /// </summary>
    Task<Result<int>> DeleteAsync(string connectorId, string target, CancellationToken cancellationToken = default);
}

/// <summary>One disconnected source with tasks still on the list.</summary>
/// <param name="ConnectorId">The connector the tasks came through, as stored on
/// them — possibly one this build does not have.</param>
/// <param name="Target">The repository or product they came from, in the spelling
/// of its most recently changed task.</param>
/// <param name="TaskCount">How many of its tasks are on the list, every status
/// counted.</param>
/// <param name="DependentCount">How many other tasks wait on one of them, and so
/// would wait on a missing task once they are deleted.</param>
public sealed record DisconnectedSource(string ConnectorId, string Target, int TaskCount, int DependentCount);
