using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Calendar.UI;
using Backlog.UI.Components.Tasks;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The Calendar's range and its Show plans choice, kept per device in the shell's own
/// navigation file beside the view it reopens on — the Calendar's
/// <see cref="ICalendarViewPreferences"/> (local ADR 0055 §1, §4).
/// <para>
/// The names are the ones the file always used, <c>calendarRange</c> and
/// <c>calendarPlansShown</c>, so a file written before the Calendar was a module of its own
/// reads back unchanged. A range this build does not define — a newer build's, or a
/// number — reads as Month, and is kept in the file by the store.
/// </para>
/// </summary>
public sealed class ShellCalendarViewPreferences(ShellNavigationStore store) : ICalendarViewPreferences
{
    public CalendarRange Range => ShellTasksViewPreferences.Remembered(store.CalendarRange, CalendarRange.Month);

    public bool PlansShown => store.CalendarPlansShown;

    public void SetRange(CalendarRange range) => store.SetCalendarRange(range.ToString());

    public void SetPlansShown(bool shown) => store.SetCalendarPlansShown(shown);
}
