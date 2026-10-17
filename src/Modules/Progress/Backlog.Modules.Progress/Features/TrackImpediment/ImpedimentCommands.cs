using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
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

/// <summary>Every change a person makes to an impediment, each one load–change–save under
/// the progress gate. "Today" is the host's clock in local time.</summary>
public sealed class ImpedimentCommandHandler(ImpedimentChanges changes, TimeProvider clock)
    : ICommandHandler<RaiseImpedimentCommand, Result<ImpedimentDto>>,
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
