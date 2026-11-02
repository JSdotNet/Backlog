using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Abstractions.Services;

/// <summary>
/// Everything a host may do with impediments, in one port the screens and the MCP tools
/// share (<c>.devbook/domain/progress/features.md#tracking-an-impediment</c>): raise one,
/// move it along its states, set the day to check again, edit its note, link and unlink
/// the tasks, pull requests and plans it holds back, and list the open ones with those
/// resolved in a window.
/// <para>
/// No call changes a task. An impediment names what it holds back by identity alone (local
/// ADR 0035 §6), and a move records the day off the host's clock, in local time.
/// </para>
/// </summary>
public interface IImpediments
{
    /// <summary>Raised after an impediment was stored on this machine, whoever stored it:
    /// a person's change, an agent's, or a copy the sync took from another device. It may
    /// arrive on any thread.</summary>
    event Action? Changed;

    /// <summary>Every open impediment, and every one resolved from
    /// <paramref name="resolvedFrom"/> to <paramref name="resolvedTo"/>, both days included,
    /// in the order they were raised. Without a window, the open ones alone.</summary>
    Task<Result<IReadOnlyList<ImpedimentDto>>> ListAsync(
        DateOnly? resolvedFrom = null,
        DateOnly? resolvedTo = null,
        CancellationToken cancellationToken = default);

    Task<Result<ImpedimentDto>> GetAsync(Guid impedimentId, CancellationToken cancellationToken = default);

    /// <summary>Raises a New impediment, dated today, holding back the items the request
    /// names. Refused without a title (<c>progress.impediment_without_title</c>), and for an
    /// item that does not name its whole identity (<c>progress.held_back_item_without_identity</c>).</summary>
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

    /// <summary>Links a task, pull request or plan the impediment holds back. Linking one
    /// it holds back already changes nothing. Refused for an item that does not name its
    /// whole identity (<c>progress.held_back_item_without_identity</c>).</summary>
    Task<Result<ImpedimentDto>> HoldBackAsync(Guid impedimentId, HeldBackItemDto item, CancellationToken cancellationToken = default);

    /// <summary>Unlinks an item. Unlinking one it does not hold back changes nothing.
    /// Refused, as linking is, for an item that does not name its whole identity.</summary>
    Task<Result<ImpedimentDto>> ReleaseAsync(Guid impedimentId, HeldBackItemDto item, CancellationToken cancellationToken = default);
}
