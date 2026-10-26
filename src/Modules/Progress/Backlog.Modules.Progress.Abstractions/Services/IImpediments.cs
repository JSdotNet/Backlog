using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Abstractions.Services;

/// <summary>
/// Everything a host may do with impediments, in one port the screens and the MCP tools
/// share (<c>.devbook/domain/progress/features.md#tracking-an-impediment</c>): raise one,
/// move it along its states, set the day to check again, edit its note, link and unlink
/// the tasks it holds back, and list the open ones with those resolved in a window.
/// <para>
/// No call changes a task. An impediment names the tasks it holds back by id (local ADR
/// 0035 §6), and a move records the day off the host's clock, in local time.
/// </para>
/// </summary>
public interface IImpediments
{
    /// <summary>Raised after an impediment was stored on this machine, whoever stored it:
    /// a person's change, the blocked-task policy, or a copy the sync took from another
    /// device. It may arrive on any thread.</summary>
    event Action? Changed;

    /// <summary>Every open impediment, and every one resolved from
    /// <paramref name="resolvedFrom"/> to <paramref name="resolvedTo"/>, both days included,
    /// in the order they were raised. Without a window, the open ones alone.</summary>
    Task<Result<IReadOnlyList<ImpedimentDto>>> ListAsync(
        DateOnly? resolvedFrom = null,
        DateOnly? resolvedTo = null,
        CancellationToken cancellationToken = default);

    Task<Result<ImpedimentDto>> GetAsync(Guid impedimentId, CancellationToken cancellationToken = default);

    /// <summary>Raises a New impediment, dated today, holding back the tasks the request
    /// names. Refused without a title (<c>progress.impediment_without_title</c>).</summary>
    Task<Result<ImpedimentDto>> RaiseAsync(RaiseImpedimentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves an impediment to <paramref name="state"/> today, recording who it is with at
    /// that step. Forward only — New to Reported, Reported to Escalated or Being handled,
    /// Escalated to Being handled — and any open state to Resolved; every other move is
    /// refused (<c>progress.impediment_move_not_allowed</c>, or
    /// <c>progress.impediment_resolved</c> for a resolved one).
    /// </summary>
    Task<Result<ImpedimentDto>> MoveAsync(
        Guid impedimentId,
        ImpedimentState state,
        string? with = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sets the day to look at the impediment again, or clears it with null.</summary>
    Task<Result<ImpedimentDto>> SetCheckAgainAsync(Guid impedimentId, DateOnly? checkAgain, CancellationToken cancellationToken = default);

    /// <summary>Replaces the impediment's note, or clears it with null or blank text.</summary>
    Task<Result<ImpedimentDto>> EditNoteAsync(Guid impedimentId, string? note, CancellationToken cancellationToken = default);

    /// <summary>Links a task the impediment holds back. Linking one it holds back already
    /// changes nothing. Refused for the empty id (<c>progress.task_required</c>).</summary>
    Task<Result<ImpedimentDto>> HoldBackAsync(Guid impedimentId, Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>Unlinks a task. Unlinking one it does not hold back changes nothing.</summary>
    Task<Result<ImpedimentDto>> ReleaseAsync(Guid impedimentId, Guid taskId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The tasks marked blocked by hand, read from Tasks for the blocked-task policy
/// (<c>.devbook/domain/progress/dependencies.md</c>, Progress = customer). Answered by an
/// adapter that reads Tasks' published surface only; Progress keeps nothing of it.
/// </summary>
public interface IBlockedTaskSource
{
    /// <summary>Every live task that carries the blocked mark and is not finished.</summary>
    Task<IReadOnlyList<BlockedTaskDto>> ReadBlockedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The <c>.devbook/domain/progress/domain.md#blocked-task-policy</c>: a task marked
/// blocked raises a New impediment titled with the task's title that holds the task back,
/// unless an open impediment holds it back already. Run by the host whenever a task was
/// written on this machine; Tasks never calls it.
/// </summary>
public interface IBlockedTaskPolicy
{
    /// <summary>
    /// Reads the blocked tasks and raises one New impediment for each that needs one,
    /// answering those raised. A task needs one when no impediment holds it back that is
    /// open, or that was raised on or after the day the task was marked blocked — so an
    /// impediment resolved while its task is still blocked is not raised again, and
    /// clearing the mark resolves nothing.
    /// </summary>
    Task<IReadOnlyList<ImpedimentDto>> ReactAsync(CancellationToken cancellationToken = default);
}
