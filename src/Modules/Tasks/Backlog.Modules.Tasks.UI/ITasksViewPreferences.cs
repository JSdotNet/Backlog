using Backlog.UI.Components.Tasks;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// How the Board's columns and the Calendar's range were left on this device, kept by
/// the host beside the view the shell reopens on.
/// </summary>
public interface ITasksViewPreferences
{
    /// <summary>What the Board's columns are grouped by.</summary>
    TaskBoardGrouping BoardGrouping { get; }

    /// <summary>The Calendar's Month | Week range.</summary>
    CalendarRange CalendarRange { get; }

    void SetBoardGrouping(TaskBoardGrouping grouping);

    void SetCalendarRange(CalendarRange range);
}
