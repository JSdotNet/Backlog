namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// How the Tasks list groups its rows, picked from the <b>Plans / Flat</b> switch on
/// its filter bar (local ADR 0062 §1).
/// <para>
/// The shell remembers the member's name beside the Board's columns, as
/// <c>tasksGrouping</c> in <c>shell-navigation.json</c>, so a name here is a stored
/// value: rename one and a remembered choice falls back to Plans.
/// </para>
/// </summary>
public enum TaskListGrouping
{
    /// <summary>By plan: the plan rail beside the list. The first-time choice.</summary>
    Plans,

    /// <summary>One flat list, the plans on the tag strip as chips.</summary>
    Flat
}
