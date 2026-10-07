namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// What the Board's columns are, picked from the "Columns" select on its filter
/// bar.
/// <para>
/// The shell remembers the member's name beside the view it belongs to, as
/// <c>boardColumns</c> in <c>shell-navigation.json</c>, so a name here is a
/// stored value: rename one and a remembered choice falls back to Status.
/// </para>
/// </summary>
public enum TaskBoardGrouping
{
    /// <summary>Draft, Ready, In progress and Done — the lifecycle, and the one
    /// grouping a card can be dragged across.</summary>
    Status,

    /// <summary>One column per plan tag in view, then "No plan".</summary>
    Plan,

    /// <summary>One column per repository in view, then "No repository".</summary>
    Repository,

    /// <summary>Critical, High, Medium and Low.</summary>
    Priority
}
