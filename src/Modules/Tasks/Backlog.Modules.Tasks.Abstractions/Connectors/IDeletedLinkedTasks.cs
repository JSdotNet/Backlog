using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Abstractions.Connectors;

/// <summary>
/// The deleted linked tasks of a source, as the connector settings page reaches
/// them: which sources have tasks deleted, and bringing one connected target's back
/// in one step.
/// <para>
/// The sync brings back on its own the tasks deleted with their source
/// (<see cref="LinkedTaskFlags.DeletedWithSource"/>) once that source is connected
/// again. This is for the rest: a task deleted on its own, and one deleted with its
/// source before deletions said so. Restoring writes nothing to the source; the
/// next sync refreshes the tasks from it like any other.
/// </para>
/// <para>
/// A sibling of <see cref="IDisconnectedLinkedTasks"/> rather than more of it: that
/// port is about the sources no longer connected, and this one acts on a connected
/// target's card.
/// </para>
/// </summary>
public interface IDeletedLinkedTasks
{
    /// <summary>Every connector-and-target pair with a deleted linked task, connected
    /// or not, ordered by connector id and then target.</summary>
    Task<IReadOnlyList<DeletedLinkedSource>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores every deleted task the pair brought in, however it was deleted, and
    /// answers how many. Refused, with nothing restored, when the pair is not
    /// connected by the time this runs: the tasks would come back as a disconnected
    /// source's, which is not what was confirmed.
    /// </summary>
    Task<Result<int>> RestoreAsync(string connectorId, string target, CancellationToken cancellationToken = default);
}

/// <summary>One source with deleted linked tasks.</summary>
/// <param name="ConnectorId">The connector the tasks came through, as stored on
/// them.</param>
/// <param name="Target">The repository or product they came from, in the spelling
/// of its most recently deleted task.</param>
/// <param name="TaskCount">How many of its tasks are deleted.</param>
public sealed record DeletedLinkedSource(string ConnectorId, string Target, int TaskCount);
