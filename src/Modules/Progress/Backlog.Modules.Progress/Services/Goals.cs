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
/// The published <see cref="IGoals"/> port over the module's goal handlers. Goals are always
/// on: no feature switch stands in front of them (local ADR 0035 §10).
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

    public async Task<Result<IReadOnlyList<GoalSummaryDto>>> ListAsync(
        DateOnly? windowStart = null,
        DateOnly? windowEnd = null,
        CancellationToken cancellationToken = default) =>
        Result.Success(await read.Handle(new ListGoalsQuery(windowStart, windowEnd), cancellationToken).ConfigureAwait(false));

    public Task<Result<GoalDto>> GetAsync(Guid goalId, CancellationToken cancellationToken = default) =>
        read.Handle(new GetGoalQuery(goalId), cancellationToken);

    public Task<Result<GoalDto?>> FindGoalOfAsync(GoalItemDto item, CancellationToken cancellationToken = default) =>
        read.Handle(new FindGoalOfQuery(item), cancellationToken);

    public Task<Result<GoalDto>> CreateAsync(SetGoalRequest request, CancellationToken cancellationToken = default) =>
        set.Handle(new CreateGoalCommand(request), cancellationToken);

    public Task<Result<GoalDto>> EditAsync(Guid goalId, SetGoalRequest request, CancellationToken cancellationToken = default) =>
        set.Handle(new EditGoalCommand(goalId, request), cancellationToken);

    public Task<Result> DeleteAsync(Guid goalId, CancellationToken cancellationToken = default) =>
        set.Handle(new DeleteGoalCommand(goalId), cancellationToken);

    public Task<Result> MoveAsync(Guid goalId, int toIndex, CancellationToken cancellationToken = default) =>
        set.Handle(new MoveGoalCommand(goalId, toIndex), cancellationToken);

    public Task<Result<GoalDto>> AttachAsync(
        Guid goalId,
        GoalItemDto item,
        bool moveFromOtherGoal = false,
        CancellationToken cancellationToken = default) =>
        attach.Handle(new AttachToGoalCommand(goalId, item, moveFromOtherGoal), cancellationToken);

    public Task<Result<GoalDto>> DetachAsync(Guid goalId, GoalItemDto item, CancellationToken cancellationToken = default) =>
        attach.Handle(new DetachFromGoalCommand(goalId, item), cancellationToken);
}
