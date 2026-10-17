using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Features.AttachWork;
using Backlog.Modules.Progress.Features.ListGoals;
using Backlog.Modules.Progress.Features.SetGoal;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// The published <see cref="IGoals"/> port over the module's goal handlers, behind the
/// <see cref="ProgressFeatures.Goals"/> switch: while it is off every call answers
/// <see cref="ProgressErrors.GoalsOff"/> and nothing is read or written. A host that
/// composed no feature settings has goals off, as they ship.
/// <para>
/// <see cref="Changed"/> is held on <see cref="GoalSaved"/>, the singleton every save
/// raises, so a screen holding this scoped port hears a goal saved through another scope
/// or pulled by the sync.
/// </para>
/// </summary>
public sealed class Goals(
    ListGoalsQueryHandler read,
    SetGoalCommandHandler set,
    AttachWorkCommandHandler attach,
    IAppFeatureSettings? features = null,
    GoalSaved? saved = null)
    : IGoals
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

    public async Task<Result<IReadOnlyList<GoalSummaryDto>>> ListAsync(
        DateOnly? windowStart = null,
        DateOnly? windowEnd = null,
        CancellationToken cancellationToken = default) =>
        IsOn
            ? Result.Success(await read.Handle(new ListGoalsQuery(windowStart, windowEnd), cancellationToken).ConfigureAwait(false))
            : Result.Failure<IReadOnlyList<GoalSummaryDto>>(ProgressErrors.GoalsOff());

    public Task<Result<GoalDto>> GetAsync(Guid goalId, CancellationToken cancellationToken = default) =>
        Gate(() => read.Handle(new GetGoalQuery(goalId), cancellationToken));

    public Task<Result<GoalDto?>> FindGoalOfAsync(GoalItemDto item, CancellationToken cancellationToken = default) =>
        Gate(() => read.Handle(new FindGoalOfQuery(item), cancellationToken));

    public Task<Result<GoalDto>> CreateAsync(SetGoalRequest request, CancellationToken cancellationToken = default) =>
        Gate(() => set.Handle(new CreateGoalCommand(request), cancellationToken));

    public Task<Result<GoalDto>> EditAsync(Guid goalId, SetGoalRequest request, CancellationToken cancellationToken = default) =>
        Gate(() => set.Handle(new EditGoalCommand(goalId, request), cancellationToken));

    public Task<Result> DeleteAsync(Guid goalId, CancellationToken cancellationToken = default) =>
        IsOn ? set.Handle(new DeleteGoalCommand(goalId), cancellationToken) : Task.FromResult(Result.Failure(ProgressErrors.GoalsOff()));

    public Task<Result<GoalDto>> AttachAsync(
        Guid goalId,
        GoalItemDto item,
        bool moveFromOtherGoal = false,
        CancellationToken cancellationToken = default) =>
        Gate(() => attach.Handle(new AttachToGoalCommand(goalId, item, moveFromOtherGoal), cancellationToken));

    public Task<Result<GoalDto>> DetachAsync(Guid goalId, GoalItemDto item, CancellationToken cancellationToken = default) =>
        Gate(() => attach.Handle(new DetachFromGoalCommand(goalId, item), cancellationToken));

    private Task<Result<T>> Gate<T>(Func<Task<Result<T>>> call) =>
        IsOn ? call() : Task.FromResult(Result.Failure<T>(ProgressErrors.GoalsOff()));
}
