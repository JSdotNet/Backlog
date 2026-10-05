namespace Backlog.UI.Components.Inputs;

/// <summary>A press on a <see cref="DayCalendar"/> date: the date, and whether Shift was
/// held so the host can take it as the far end of a range.</summary>
public sealed record DayCalendarPress(DateOnly Date, bool Extend);
