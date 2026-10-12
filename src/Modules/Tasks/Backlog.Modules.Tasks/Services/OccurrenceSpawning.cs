using System.Security.Cryptography;
using System.Text;

using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Modules.Tasks.Services;

/// <summary>
/// Occurrence Spawning (<c>.devbook/domain/tasks/domain.md#occurrence-spawning</c>):
/// the one place a ticked repeating task gets its next occurrence, whichever
/// machine saw the tick.
/// <para>
/// Two callers run it. The desktop's own save runs it when the save ticks the
/// task off (<c>SaveTaskFromTextCommandHandler</c>); the replica merge runs it
/// when a pulled copy ticks off a task this machine held unticked — a tick made
/// on the phone, or on another desktop. Both must make the same successor, and
/// make it once.
/// </para>
/// <para>
/// <b>Once is kept by the successor's id.</b> The id is derived from the
/// occurrence it follows (<see cref="SuccessorId"/>) rather than drawn at
/// random, so two desktops that each see the same tick spawn the same task, and
/// the replica's whole-document last write wins folds the two copies into one.
/// A successor this machine already holds — live or deleted — is not made
/// again: a re-pull, an untick and a second tick, or a tick on one desktop
/// arriving at another that already pulled the successor all find it and stop.
/// A deleted successor stays deleted, because deleting it is how the person
/// stopped that repeat.
/// </para>
/// </summary>
public static class OccurrenceSpawning
{
    /// <summary>Prefixed onto the occurrence's id before it is hashed, so the
    /// successor's id cannot collide with a hash any other rule takes of the
    /// same id.</summary>
    private const string Namespace = "backlog:occurrence-successor:";

    /// <summary>
    /// The id of the occurrence that follows <paramref name="occurrenceId"/>. The
    /// same input gives the same id on every machine: the first sixteen bytes of
    /// a SHA-256 over the namespace and the id, marked as an RFC 9562 version 8
    /// (custom) UUID so it never reads as a random or time-based one.
    /// </summary>
    public static Guid SuccessorId(Guid occurrenceId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Namespace + occurrenceId.ToString("D")));
        var bytes = hash.AsSpan(0, 16).ToArray();

        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>
    /// Spawns the next occurrence of <paramref name="completed"/> and saves it, or
    /// answers null when none is owed: the task is not ticked, carries no repeat,
    /// is deleted, or its successor already exists here.
    /// <para>
    /// The caller decides that the tick is new — the step from unticked to
    /// ticked. This decides only that the successor is owed and not yet held.
    /// Saved after the completed occurrence by every caller, so a failure here
    /// cannot lose the completion already recorded.
    /// </para>
    /// </summary>
    public static async Task<TaskItem?> SpawnAsync(
        TaskItem completed,
        ITaskRepository tasks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completed);
        ArgumentNullException.ThrowIfNull(tasks);

        if (!completed.IsCompleted || completed.Recurrence is null || completed.DeletedAt is not null) return null;

        var existing = await tasks
            .GetIncludingDeletedAsync(SuccessorId(completed.Id), cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null) return null;

        var successor = RecurrencePolicy.NextOccurrence(completed);
        await tasks.SaveAsync(successor, cancellationToken).ConfigureAwait(false);

        return successor;
    }
}
