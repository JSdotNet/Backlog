namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// How the Board's columns were left on this device, kept by the host beside the view
/// the shell reopens on. The Calendar's range is the Calendar's own view choice (local
/// ADR 0055 §1).
/// </summary>
public interface ITasksViewPreferences
{
    /// <summary>What the Board's columns are grouped by.</summary>
    TaskBoardGrouping BoardGrouping { get; }

    void SetBoardGrouping(TaskBoardGrouping grouping);
}
