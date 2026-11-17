using Backlog.UI.Components.Tasks;
using Microsoft.AspNetCore.Components;

namespace Backlog.UI.Components.Calendar;

/// <summary>
/// What the Calendar hands the Tasks part (local ADR 0055 §3): the range it keeps, the
/// Plans and Events layers it reads, and the hours worked on a day.
/// </summary>
/// <param name="Range">The Month | Week range the Calendar keeps.</param>
/// <param name="RangeChanged">Raised with the range the reader picked.</param>
/// <param name="Plans">The Plans layer as the calendar draws it.</param>
/// <param name="CapacityOn">The hours worked on a day; null when the host composed no
/// working week, when a day's planned hours are set against nothing.</param>
/// <param name="Events">The Events layer as the calendar draws it.</param>
public sealed record CalendarLayerInputs(
    CalendarRange Range,
    EventCallback<CalendarRange> RangeChanged,
    CalendarPlansInputs Plans,
    Func<DateOnly, decimal?>? CapacityOn,
    CalendarEventsInputs Events)
{
    /// <summary>Outside the Calendar: a month, no plans, no events and no capacity.</summary>
    public static CalendarLayerInputs None { get; } =
        new(CalendarRange.Month, default, CalendarPlansInputs.None, null, CalendarEventsInputs.None);
}

/// <summary>
/// The Plans layer as the calendar draws it.
/// </summary>
/// <param name="Available">Whether there is a roadmap to show; false offers no Show plans box.</param>
/// <param name="Shown">Whether the Show plans box is ticked.</param>
/// <param name="Windows">Each planned window, with the repositories it is filed under.</param>
/// <param name="Milestones">Each milestone.</param>
/// <param name="Shelf">The plans still waiting on the roadmap's shelf.</param>
/// <param name="ShownChanged">Raised when the Show plans box is ticked or unticked.</param>
/// <param name="OnStarted">Raised when a shelf plan is dropped on a day.</param>
public sealed record CalendarPlansInputs(
    bool Available,
    bool Shown,
    IReadOnlyList<CalendarPlanWindow> Windows,
    IReadOnlyList<CalendarMilestone> Milestones,
    IReadOnlyList<CalendarShelfPlan> Shelf,
    EventCallback<bool> ShownChanged,
    EventCallback<CalendarPlanStart> OnStarted)
{
    /// <summary>No Plans layer: nothing to show and no box to tick.</summary>
    public static CalendarPlansInputs None { get; } = new(false, false, [], [], [], default, default);
}

/// <summary>
/// The Events layer as the calendar draws it. Each event and key date is keyed by its place
/// in the read — <c>e0</c>, <c>k0</c> — because the port carries no event id, and the
/// Calendar opens and writes none.
/// </summary>
/// <param name="Available">Whether there are events to show; false offers no Show events
/// box.</param>
/// <param name="Shown">Whether the Show events box is ticked.</param>
/// <param name="Events">Each decided event that has a start, as a bar.</param>
/// <param name="KeyDates">Each key date, as a diamond on its day.</param>
/// <param name="ShownChanged">Raised when the Show events box is ticked or unticked.</param>
public sealed record CalendarEventsInputs(
    bool Available,
    bool Shown,
    IReadOnlyList<CalendarEvent> Events,
    IReadOnlyList<CalendarKeyDate> KeyDates,
    EventCallback<bool> ShownChanged)
{
    /// <summary>No Events layer: nothing to show and no box to tick.</summary>
    public static CalendarEventsInputs None { get; } = new(false, false, [], [], default);
}

/// <summary>
/// One planned window, with the repositories it is filed under: the Tasks part gives the
/// bar the band colour of the first one its window's colours know, so
/// <see cref="CalendarPlan.Band"/> arrives null.
/// </summary>
/// <param name="Plan">The bar, without its band colour.</param>
/// <param name="RepositoryAliases">The repositories it is filed under, by alias.</param>
public sealed record CalendarPlanWindow(CalendarPlan Plan, IReadOnlyList<string> RepositoryAliases);
