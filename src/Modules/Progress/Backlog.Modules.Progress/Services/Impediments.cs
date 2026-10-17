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
/// behind the <see cref="ProgressFeatures.Goals"/> switch the goals answer to: while it is
/// off every call answers <see cref="ProgressErrors.GoalsOff"/> and nothing is read or
/// written. <see cref="Changed"/> is held on <see cref="ImpedimentSaved"/>, the singleton
/// every save raises.
/// </summary>
public sealed class Impediments(
    ListImpedimentsQueryHandler read,
    ImpedimentCommandHandler commands,
    IAppFeatureSettings? features = null,
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

    private bool IsOn => features?.IsEnabled(ProgressFeatures.Goals) ?? false;

    public async Task<Result<IReadOnlyList<ImpedimentDto>>> ListAsync(
        DateOnly? resolvedFrom = null,
        DateOnly? resolvedTo = null,
        CancellationToken cancellationToken = default) =>
        IsOn
            ? Result.Success(await read.Handle(new ListImpedimentsQuery(resolvedFrom, resolvedTo), cancellationToken).ConfigureAwait(false))
            : Result.Failure<IReadOnlyList<ImpedimentDto>>(ProgressErrors.GoalsOff());

    public Task<Result<ImpedimentDto>> GetAsync(Guid impedimentId, CancellationToken cancellationToken = default) =>
        Gate(() => read.Handle(new GetImpedimentQuery(impedimentId), cancellationToken));

    public Task<Result<ImpedimentDto>> RaiseAsync(RaiseImpedimentRequest request, CancellationToken cancellationToken = default) =>
        Gate(() => commands.Handle(new RaiseImpedimentCommand(request), cancellationToken));

    public Task<Result<ImpedimentDto>> MoveAsync(
        Guid impedimentId,
        ImpedimentState state,
        string? with = null,
        CancellationToken cancellationToken = default) =>
        Gate(() => commands.Handle(new MoveImpedimentCommand(impedimentId, state, with), cancellationToken));

    public Task<Result<ImpedimentDto>> SetCheckAgainAsync(Guid impedimentId, DateOnly? checkAgain, CancellationToken cancellationToken = default) =>
        Gate(() => commands.Handle(new SetCheckAgainCommand(impedimentId, checkAgain), cancellationToken));

    public Task<Result<ImpedimentDto>> EditNoteAsync(Guid impedimentId, string? note, CancellationToken cancellationToken = default) =>
        Gate(() => commands.Handle(new EditImpedimentNoteCommand(impedimentId, note), cancellationToken));

    public Task<Result<ImpedimentDto>> HoldBackAsync(Guid impedimentId, Guid taskId, CancellationToken cancellationToken = default) =>
        Gate(() => commands.Handle(new HoldBackTaskCommand(impedimentId, taskId), cancellationToken));

    public Task<Result<ImpedimentDto>> ReleaseAsync(Guid impedimentId, Guid taskId, CancellationToken cancellationToken = default) =>
        Gate(() => commands.Handle(new ReleaseTaskCommand(impedimentId, taskId), cancellationToken));

    private Task<Result<T>> Gate<T>(Func<Task<Result<T>>> call) =>
        IsOn ? call() : Task.FromResult(Result.Failure<T>(ProgressErrors.GoalsOff()));
}
