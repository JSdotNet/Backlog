using global::Backlog.UI.Components.Tasks;

namespace Backlog.Modules.Calendar.UI;

/// <summary>
/// The Calendar's own view choices on this device (local ADR 0055 §1, §4): its range, its
/// Show plans choice and its Show events choice, each kept by the host under the name it
/// always used and ticked apart from the other.
/// </summary>
public interface ICalendarViewPreferences
{
    /// <summary>The Month | Week range; Month when nothing, or nothing this build
    /// defines, is stored.</summary>
    CalendarRange Range { get; }

    /// <summary>Whether the Calendar draws the roadmap's plans — its Show plans box. Off
    /// until ticked.</summary>
    bool PlansShown { get; }

    /// <summary>Whether the Calendar draws the knowledge events — its Show events box. Off
    /// until ticked.</summary>
    bool EventsShown { get; }

    void SetRange(CalendarRange range);

    void SetPlansShown(bool shown);

    void SetEventsShown(bool shown);
}
