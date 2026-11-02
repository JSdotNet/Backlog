namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The title of a task the window's backlog holds, for the tasks a devbook tab names
/// — a Brain note's Tasks section (local ADR 0026 §3), drawn on the pane through its
/// mode (local ADR 0040 §2). The tasks are another module's, so the Devbook asks
/// rather than reads them; the host answers from the window's own task list. A host
/// that registers none leaves every task a tab names unresolved — "not in this
/// backlog".
/// </summary>
public interface IDevbookTaskTitles
{
    /// <summary>The title of the task with id <paramref name="taskId"/>, or null
    /// when the backlog holds none.</summary>
    string? TitleFor(string taskId);
}
