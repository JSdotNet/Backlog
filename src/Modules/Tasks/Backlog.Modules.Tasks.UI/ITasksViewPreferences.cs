namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// How the Board's columns were left on this device, and the list's grouping in this
/// window, kept by the host beside the view the shell reopens on: a popped-out window opens
/// on the main window's remembered grouping and keeps its own (local ADR 0072 §1). The
/// Calendar's range is the Calendar's own view choice (local ADR 0055 §1).
/// </summary>
public interface ITasksViewPreferences
{
    /// <summary>What the Board's columns are grouped by.</summary>
    TaskBoardGrouping BoardGrouping { get; }

    void SetBoardGrouping(TaskBoardGrouping grouping);

    /// <summary>How the Tasks list groups in this window: by plan, or flat (local ADR 0072 §1).</summary>
    TaskListGrouping ListGrouping { get; }

    void SetListGrouping(TaskListGrouping grouping);
}
