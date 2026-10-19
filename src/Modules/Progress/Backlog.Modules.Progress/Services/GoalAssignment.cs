using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// Attaches an item to a goal, and holds the one rule that spans goals: an item belongs
/// to at most one goal (<c>.devbook/domain/progress/domain.md#goal-assignment</c>,
/// <c>domain.invariants.md#invariant-an-item-belongs-to-at-most-one-goal</c>). A service
/// because no single goal can see the others.
/// <para>
/// The rule is over attachments: a plan, a task or a pull request attached to one goal
/// is refused on another. A task that one goal holds directly while another holds its
/// plan is allowed, and counts in the goal that holds it directly — that half of the rule
/// is <see cref="GoalCounting"/>'s.
/// </para>
/// </summary>
public static class GoalAssignment
{
    /// <summary>The goal among <paramref name="all"/> that holds <paramref name="item"/>,
    /// or null.</summary>
    public static Goal? HolderOf(IEnumerable<Goal> all, GoalAttachment item)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(item);

        return all.FirstOrDefault(goal => goal.Holds(item));
    }

    /// <summary>
    /// Attaches <paramref name="item"/> to <paramref name="target"/>. Refused, with nothing
    /// changed, when another goal among <paramref name="all"/> holds it
    /// (<c>progress.item_in_other_goal</c>, naming that goal) — unless
    /// <paramref name="move"/> is set, when it is detached from that goal first. Answers
    /// the goal it moved from, which the caller saves too, or null.
    /// </summary>
    public static Result<Goal?> Assign(IReadOnlyList<Goal> all, Goal target, GoalAttachment item, bool move = false)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(item);

        var holder = HolderOf(all.Where(goal => goal.Id != target.Id), item);

        if (holder is not null)
        {
            if (!move) return ProgressErrors.ItemInOtherGoal(holder.Title);
            holder.Detach(item);
        }

        target.Attach(item);
        return holder;
    }
}
