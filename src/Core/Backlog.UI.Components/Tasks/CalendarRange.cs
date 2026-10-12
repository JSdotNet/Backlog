namespace Backlog.UI.Components.Tasks;

/// <summary>
/// How much of the calendar is on screen, picked from the Month | Week control in its
/// month bar.
/// <para>
/// The shell remembers the member's name, as <c>calendarRange</c> in
/// <c>shell-navigation.json</c>, so a name here is a stored value: rename one and a
/// remembered choice falls back to Month.
/// </para>
/// </summary>
public enum CalendarRange
{
    /// <summary>Whole Monday-first weeks spanning the month, two chips a day before
    /// "+N more".</summary>
    Month,

    /// <summary>One Monday-first week as seven tall columns, every chip shown.</summary>
    Week
}
