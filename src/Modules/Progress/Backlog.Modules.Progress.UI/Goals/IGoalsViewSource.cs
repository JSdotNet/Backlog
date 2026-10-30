using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.UI.Goals;

/// <summary>
/// What the Goals view maps from that only the window's shell holds — the task list,
/// the plan tags and the open pull requests — and how it asks the shell to read them
/// (<c>.devbook/domain/progress/features.md#the-goals-view</c>). One per window: the
/// host registers it with the window-state lifetime, and the Goals surface reads the
/// window's own. A host that composes none draws the view with no candidates.
/// </summary>
public interface IGoalsViewSource
{
    /// <summary>Raised when the candidates changed — the reader's pull requests landed,
    /// or the task list moved — so the view maps its last reading again.</summary>
    event Action? Changed;

    /// <summary>The candidates now, asked for on every mapping and never kept.</summary>
    GoalsCandidates Candidates();

    /// <summary>The view opened (false) or Refresh was pressed (true). Not awaited by the
    /// view: the board draws from what is held, and fills in when the read lands.</summary>
    Task ReadAsync(bool refresh);

    /// <summary>The window's create for a goal's "New task" row: a task titled with the
    /// text given, its id back or the Tasks module's refusal — the shell's, because
    /// Progress UI may not reach Tasks. Null, the default, offers no goal the row.</summary>
    Func<string, Task<Result<Guid>>>? CreateTask => null;
}
