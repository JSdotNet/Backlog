namespace Backlog.Desktop.UI.Tasks;

/// <summary>The Calendar as a shell surface: the Tasks view's filtered rows on a month
/// or a week by their due date, beside the same detail panel (local ADR 0041 §1).</summary>
public sealed class CalendarSurface : TasksSurface
{
    protected override TasksLayout Layout => TasksLayout.Calendar;
}
