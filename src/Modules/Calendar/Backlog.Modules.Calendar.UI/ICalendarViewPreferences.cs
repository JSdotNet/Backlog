using global::Backlog.UI.Components.Tasks;

namespace Backlog.Modules.Calendar.UI;

/// <summary>
/// The Calendar's own view choices on this device (local ADR 0055 §1, §4): its range and
/// its Show plans choice, kept by the host under the names it always used. Show events
/// stays behind <c>ICalendarEvents</c> until the Events layer moves.
/// </summary>
public interface ICalendarViewPreferences
{
    /// <summary>The Month | Week range; Month when nothing, or nothing this build
    /// defines, is stored.</summary>
    CalendarRange Range { get; }

    /// <summary>Whether the Calendar draws the roadmap's plans — its Show plans box. Off
    /// until ticked.</summary>
    bool PlansShown { get; }

    void SetRange(CalendarRange range);

    void SetPlansShown(bool shown);
}
