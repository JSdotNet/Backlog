using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Features.ListImpediments;
using Backlog.Modules.Progress.Features.TrackImpediment;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// The published <see cref="IImpediments"/> port over the module's impediment handlers,
/// always on as the goals are (local ADR 0035 §10). <see cref="Changed"/> is held on
/// <see cref="ImpedimentSaved"/>, the singleton every save raises.
/// </summary>
public sealed class Impediments(
    ListImpedimentsQueryHandler read,
    ImpedimentCommandHandler commands,
    ImpedimentSaved? saved = null)
    : IImpediments
{
    public event Action? Changed
    {
        add
        {
            if (saved is not null) saved.Changed += value;
        }
        remove
        {
            if (saved is not null) saved.Changed -= value;
        }
    }

    public async Task<Result<IReadOnlyList<ImpedimentDto>>> ListAsync(
        DateOnly? resolvedFrom = null,
        DateOnly? resolvedTo = null,
        CancellationToken cancellationToken = default) =>
        Result.Success(await read.Handle(new ListImpedimentsQuery(resolvedFrom, resolvedTo), cancellationToken).ConfigureAwait(false));

    public Task<Result<ImpedimentDto>> GetAsync(Guid impedimentId, CancellationToken cancellationToken = default) =>
        read.Handle(new GetImpedimentQuery(impedimentId), cancellationToken);

    public Task<Result<ImpedimentDto>> RaiseAsync(RaiseImpedimentRequest request, CancellationToken cancellationToken = default) =>
        commands.Handle(new RaiseImpedimentCommand(request), cancellationToken);

    public Task<Result<ImpedimentDto>> MoveAsync(
        Guid impedimentId,
        ImpedimentState state,
        string? with = null,
        CancellationToken cancellationToken = default) =>
        commands.Handle(new MoveImpedimentCommand(impedimentId, state, with), cancellationToken);

    public Task<Result<ImpedimentDto>> SetCheckAgainAsync(Guid impedimentId, DateOnly? checkAgain, CancellationToken cancellationToken = default) =>
        commands.Handle(new SetCheckAgainCommand(impedimentId, checkAgain), cancellationToken);

    public Task<Result<ImpedimentDto>> EditNoteAsync(Guid impedimentId, string? note, CancellationToken cancellationToken = default) =>
        commands.Handle(new EditImpedimentNoteCommand(impedimentId, note), cancellationToken);

    public Task<Result<ImpedimentDto>> HoldBackAsync(Guid impedimentId, Guid taskId, CancellationToken cancellationToken = default) =>
        commands.Handle(new HoldBackTaskCommand(impedimentId, taskId), cancellationToken);

    public Task<Result<ImpedimentDto>> ReleaseAsync(Guid impedimentId, Guid taskId, CancellationToken cancellationToken = default) =>
        commands.Handle(new ReleaseTaskCommand(impedimentId, taskId), cancellationToken);
}
