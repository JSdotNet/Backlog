using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.DomainModels;
using Backlog.Modules.Progress.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Features.TrackImpediment;

/// <summary>Raises a New impediment today
/// (<c>.devbook/domain/progress/features.md#tracking-an-impediment</c>).</summary>
public sealed record RaiseImpedimentCommand(RaiseImpedimentRequest Request);

/// <summary>Moves an impediment to <paramref name="State"/> today, with who it is with.</summary>
public sealed record MoveImpedimentCommand(Guid ImpedimentId, ImpedimentState State, string? With);

/// <summary>Sets or clears the day to look at an impediment again.</summary>
public sealed record SetCheckAgainCommand(Guid ImpedimentId, DateOnly? CheckAgain);

/// <summary>Replaces or clears an impediment's note.</summary>
public sealed record EditImpedimentNoteCommand(Guid ImpedimentId, string? Note);

/// <summary>Links a task an impediment holds back.</summary>
public sealed record HoldBackTaskCommand(Guid ImpedimentId, Guid TaskId);

/// <summary>Unlinks a task an impediment held back.</summary>
public sealed record ReleaseTaskCommand(Guid ImpedimentId, Guid TaskId);

/// <summary>Applies one edit the phone asked for (local ADR 0035 §8), while the field it
/// changes still says what the phone saw.</summary>
public sealed record ApplyImpedimentEditCommand(ImpedimentEditDto Edit);

/// <summary>Every change a person makes to an impediment, each one load–change–save under
/// the progress gate. "Today" is the host's clock in local time.</summary>
public sealed class ImpedimentCommandHandler(ImpedimentChanges changes, TimeProvider clock)
    : ICommandHandler<ApplyImpedimentEditCommand, ImpedimentEditOutcome>,
      ICommandHandler<RaiseImpedimentCommand, Result<ImpedimentDto>>,
      ICommandHandler<MoveImpedimentCommand, Result<ImpedimentDto>>,
      ICommandHandler<SetCheckAgainCommand, Result<ImpedimentDto>>,
      ICommandHandler<EditImpedimentNoteCommand, Result<ImpedimentDto>>,
      ICommandHandler<HoldBackTaskCommand, Result<ImpedimentDto>>,
      ICommandHandler<ReleaseTaskCommand, Result<ImpedimentDto>>
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<Result<ImpedimentDto>> Handle(RaiseImpedimentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);

        var raised = Impediment.Raise(Guid.NewGuid(), command.Request.Title, Today, clock.GetUtcNow());
        if (raised.IsFailure) return raised.Error;

        var impediment = raised.Value;
        foreach (var task in command.Request.HoldsBack ?? [])
        {
            var linked = impediment.HoldBack(task);
            if (linked.IsFailure) return linked.Error;
        }

        impediment.EditNote(command.Request.Note);

        return await changes.AddAsync(impediment, cancellationToken).ConfigureAwait(false);
    }

    public Task<Result<ImpedimentDto>> Handle(MoveImpedimentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var today = Today;
        return changes.ChangeAsync(command.ImpedimentId, impediment => impediment.MoveTo(command.State, today, command.With), cancellationToken);
    }

    public Task<Result<ImpedimentDto>> Handle(SetCheckAgainCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return changes.ChangeAsync(
            command.ImpedimentId,
            impediment =>
            {
                impediment.SetCheckAgain(command.CheckAgain);
                return Result.Success();
            },
            cancellationToken);
    }

    public Task<Result<ImpedimentDto>> Handle(EditImpedimentNoteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return changes.ChangeAsync(
            command.ImpedimentId,
            impediment =>
            {
                impediment.EditNote(command.Note);
                return Result.Success();
            },
            cancellationToken);
    }

    public Task<Result<ImpedimentDto>> Handle(HoldBackTaskCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return changes.ChangeAsync(command.ImpedimentId, impediment => impediment.HoldBack(command.TaskId), cancellationToken);
    }

    /// <summary>
    /// The phone's edit, as one compare-and-set under the gate: an impediment that already says
    /// what the edit asks is <see cref="ImpedimentEditOutcome.Unchanged"/>; one whose field no
    /// longer says what the phone saw is <see cref="ImpedimentEditOutcome.Stale"/>, so a change
    /// made elsewhere since stands; otherwise the move, the check-again date or the note is
    /// applied as the person's own would be. Nothing is written unless it is applied.
    /// </summary>
    public async Task<ImpedimentEditOutcome> Handle(ApplyImpedimentEditCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Edit);

        var edit = command.Edit;
        var today = Today;
        var outcome = ImpedimentEditOutcome.Applied;

        var changed = await changes.ChangeAsync(
            edit.ImpedimentId,
            impediment =>
            {
                if (Says(impediment, edit)) outcome = ImpedimentEditOutcome.Unchanged;
                else if (!StillAsSeen(impediment, edit)) outcome = ImpedimentEditOutcome.Stale;
                if (outcome != ImpedimentEditOutcome.Applied) return Result.Failure(ProgressErrors.ImpedimentEditNotApplied(edit.ImpedimentId));

                switch (edit.Kind)
                {
                    case ImpedimentEditKind.State:
                        var moved = impediment.MoveTo(edit.State!.Value, today, Clean(edit.With));
                        if (moved.IsFailure) outcome = ImpedimentEditOutcome.Refused;
                        return moved;

                    case ImpedimentEditKind.CheckAgain:
                        impediment.SetCheckAgain(edit.CheckAgain);
                        return Result.Success();

                    default:
                        impediment.EditNote(edit.Note);
                        return Result.Success();
                }
            },
            cancellationToken).ConfigureAwait(false);

        if (changed.IsSuccess) return ImpedimentEditOutcome.Applied;
        return changed.Error.Code == ProgressErrors.ImpedimentNotFound(edit.ImpedimentId).Code ? ImpedimentEditOutcome.Unknown : outcome;
    }

    /// <summary>Whether the impediment already says what <paramref name="edit"/> asks: in the
    /// state asked, on the check-again day asked, or carrying the note asked.</summary>
    private static bool Says(Impediment impediment, ImpedimentEditDto edit) => edit.Kind switch
    {
        ImpedimentEditKind.State => impediment.State == edit.State,
        ImpedimentEditKind.CheckAgain => impediment.CheckAgain == edit.CheckAgain,
        _ => SameNote(impediment.Note, edit.Note),
    };

    /// <summary>Whether the field still says what the phone saw. A state change that names no
    /// state seen is taken as seen, so it is decided by the states' own rules alone.</summary>
    private static bool StillAsSeen(Impediment impediment, ImpedimentEditDto edit) => edit.Kind switch
    {
        ImpedimentEditKind.State => edit.WasState is not { } seen || impediment.State == seen,
        ImpedimentEditKind.CheckAgain => impediment.CheckAgain == edit.WasCheckAgain,
        _ => SameNote(impediment.Note, edit.WasNote),
    };

    private static bool SameNote(string? left, string? right) =>
        string.Equals(Clean(left) ?? string.Empty, Clean(right) ?? string.Empty, StringComparison.Ordinal);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public Task<Result<ImpedimentDto>> Handle(ReleaseTaskCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return changes.ChangeAsync(
            command.ImpedimentId,
            impediment =>
            {
                impediment.Release(command.TaskId);
                return Result.Success();
            },
            cancellationToken);
    }
}
