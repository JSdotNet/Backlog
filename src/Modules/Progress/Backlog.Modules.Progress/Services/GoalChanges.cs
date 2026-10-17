using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// The load–change–save every command over goals shares, as <see cref="SeriesChanges"/> is
/// for a series: under the <see cref="ProgressGate"/>, load every goal, apply the change,
/// and save only the goals it changed, only when it succeeded — a refused change leaves
/// every stored goal as it was. Every goal is loaded because the one-goal rule spans them.
/// </summary>
public sealed class GoalChanges(IGoalRepository repository, ProgressGate gate, GoalSaved saved)
{
    /// <summary>Changes goal <paramref name="goalId"/>. The change answers the goals it
    /// changed besides that one — the goal an item moved from — to save with it.</summary>
    public async Task<Result<GoalDto>> ChangeAsync(
        Guid goalId,
        Func<IReadOnlyList<Goal>, Goal, Result<IReadOnlyList<Goal>>> change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        using var held = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        var all = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var goal = all.FirstOrDefault(candidate => candidate.Id == goalId);
        if (goal is null) return ProgressErrors.GoalNotFound(goalId);

        var changed = change(all, goal);
        if (changed.IsFailure) return changed.Error;

        foreach (var other in changed.Value.Where(other => other.Id != goal.Id))
        {
            await repository.SaveAsync(other, cancellationToken).ConfigureAwait(false);
        }

        await repository.SaveAsync(goal, cancellationToken).ConfigureAwait(false);
        held.Dispose(); // Let go first, so nothing that hears the notice waits on this write.
        saved.Raise();
        return goal.ToDto();
    }

    /// <summary>Saves a new goal under the gate and announces it.</summary>
    public async Task<Result<GoalDto>> AddAsync(Goal goal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(goal);

        using var held = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await repository.SaveAsync(goal, cancellationToken).ConfigureAwait(false);
        held.Dispose();
        saved.Raise();
        return goal.ToDto();
    }

    /// <summary>Deletes a goal under the gate and announces it as a save is announced, so a
    /// sync loop pushes the deletion. Refused for a goal that is not there.</summary>
    public async Task<Result> DeleteAsync(Guid goalId, CancellationToken cancellationToken = default)
    {
        using var held = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        if (await repository.GetAsync(goalId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure(ProgressErrors.GoalNotFound(goalId));
        }

        await repository.DeleteAsync(goalId, cancellationToken).ConfigureAwait(false);
        held.Dispose();
        saved.Raise();
        return Result.Success();
    }
}
