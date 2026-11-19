using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.UI.WeeklyReview;

/// <summary>
/// What the weekly review reads and does through the window's shell
/// (<c>.devbook/domain/progress/features.md#the-weekly-review</c>): the Inbox, the Brain,
/// the task list, the roadmap and the goals, none of which Progress UI may reach. One per
/// window, registered by the host with the window-state lifetime, as
/// <c>IGoalsViewSource</c> is. A host that composes none draws the review with nothing in
/// it and every source said to be unavailable.
/// </summary>
public interface IWeeklyReviewSource
{
    /// <summary>Raised when something the review reads changed elsewhere — the shell's
    /// raises it when the Brain's setting moves or its index is rebuilt — so it reads
    /// again. Anything else is read again on Refresh.</summary>
    event Action? Changed;

    /// <summary>Everything the review checks, read now and never kept.</summary>
    Task<WeeklyReviewInputs> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Carries out one line's action: opens the Inbox, a note, a task, the goals or a plan
    /// in this window, marks a plan's next action, or moves a note to the archive. A
    /// refusal comes back as a failure with the reason, said on the review.
    /// </summary>
    Task<Result> ActAsync(WeeklyReviewAction action, CancellationToken cancellationToken = default);
}
