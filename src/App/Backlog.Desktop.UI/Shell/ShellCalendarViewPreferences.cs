using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Calendar.UI;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The Calendar's range and its Show tasks, Show plans and Show events choices, kept per
/// device in the shell's own navigation file beside the view it reopens on — the
/// Calendar's <see cref="ICalendarViewPreferences"/> (local ADR 0055 §1, §4; local ADR 0056
/// §4).
/// <para>
/// The names are the ones the file always used, <c>calendarRange</c>,
/// <c>calendarPlansShown</c> and <c>calendarEventsShown</c>, so a file written before the
/// Calendar was a module of its own reads back unchanged; <c>calendarTasksShown</c> is
/// new, and absent reads as on. A range this build does not define — a newer build's, or
/// a number — reads as Month, and is kept in the file by the store.
/// </para>
/// </summary>
public sealed class ShellCalendarViewPreferences(ShellNavigationStore store) : ICalendarViewPreferences
{
    public CalendarRange Range => ShellTasksViewPreferences.Remembered(store.CalendarRange, CalendarRange.Month);

    public bool TasksShown => store.CalendarTasksShown;

    public bool PlansShown => store.CalendarPlansShown;

    public bool EventsShown => store.CalendarEventsShown;

    public void SetRange(CalendarRange range) => store.SetCalendarRange(range.ToString());

    public void SetTasksShown(bool shown) => store.SetCalendarTasksShown(shown);

    public void SetPlansShown(bool shown) => store.SetCalendarPlansShown(shown);

    public void SetEventsShown(bool shown) => store.SetCalendarEventsShown(shown);
}
