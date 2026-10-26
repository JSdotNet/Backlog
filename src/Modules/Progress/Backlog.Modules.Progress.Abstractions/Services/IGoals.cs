using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Abstractions.Services;

/// <summary>
/// Everything a host may do with goals, in one port the screens and the MCP tools share:
/// set, edit and delete a goal, attach and detach its work, and read the goals with their
/// counts (<c>.devbook/domain/progress/features.md#setting-a-goal</c>,
/// <c>#attaching-work-to-a-goal</c>). A plain delegation to the module's use-case
/// handlers, always on (local ADR 0035 §10).
/// <para>
/// No call changes a task, a plan or a pull request. A goal names its work by identity
/// and reads its state on every reading (local ADR 0035 §6).
/// </para>
/// </summary>
public interface IGoals
{
    /// <summary>
    /// Raised after a goal was stored on this machine, whoever stored it: a person's
    /// change through any screen or scope, or a copy the sync took from another device
    /// (<see cref="IGoalReplication.ApplyAsync"/>). A refused change stored nothing and
    /// raises nothing. It may arrive on any thread.
    /// </summary>
    event Action? Changed;

    /// <summary>
    /// Every goal, in the order they were set, each with its
    /// <see cref="GoalProgressDto"/> read from the current state of its work.
    /// <see cref="GoalProgressDto.DoneInWindow"/> counts the items done from
    /// <paramref name="windowStart"/> to <paramref name="windowEnd"/>, both days
    /// included, and is 0 without a window.
    /// </summary>
    Task<Result<IReadOnlyList<GoalSummaryDto>>> ListAsync(
        DateOnly? windowStart = null,
        DateOnly? windowEnd = null,
        CancellationToken cancellationToken = default);

    Task<Result<GoalDto>> GetAsync(Guid goalId, CancellationToken cancellationToken = default);

    /// <summary>The goal that holds <paramref name="item"/>, or null when none does — the
    /// question an attach button asks before it offers to attach.</summary>
    Task<Result<GoalDto?>> FindGoalOfAsync(GoalItemDto item, CancellationToken cancellationToken = default);

    /// <summary>Sets a new goal, holding nothing yet, with or without a due date. Refused
    /// without a title (<c>progress.goal_without_title</c>), or with an outcome of more than
    /// one line (<c>progress.outcome_not_one_line</c>).</summary>
    Task<Result<GoalDto>> CreateAsync(SetGoalRequest request, CancellationToken cancellationToken = default);

    /// <summary>Replaces a goal's title, outcome and due date, on the rules
    /// <see cref="CreateAsync"/> holds. What it holds is unchanged.</summary>
    Task<Result<GoalDto>> EditAsync(Guid goalId, SetGoalRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes a goal and its attachments. The plans, tasks and pull requests it
    /// held are unchanged and belong to no goal from then on.</summary>
    Task<Result> DeleteAsync(Guid goalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches a plan, a task or a pull request to a goal. Attaching what the goal holds
    /// already changes nothing. An item another goal holds is refused, naming that goal
    /// (<c>progress.item_in_other_goal</c>), unless <paramref name="moveFromOtherGoal"/>
    /// is set — then it leaves that goal and joins this one in the same change, so it is
    /// never in two.
    /// </summary>
    Task<Result<GoalDto>> AttachAsync(
        Guid goalId,
        GoalItemDto item,
        bool moveFromOtherGoal = false,
        CancellationToken cancellationToken = default);

    /// <summary>Detaches an item from a goal. Detaching what the goal does not hold
    /// changes nothing.</summary>
    Task<Result<GoalDto>> DetachAsync(Guid goalId, GoalItemDto item, CancellationToken cancellationToken = default);
}
