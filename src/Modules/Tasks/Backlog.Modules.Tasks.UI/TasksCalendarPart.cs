using Backlog.UI.Components.Calendar;
using Microsoft.AspNetCore.Components;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The Tasks layer as a part the Calendar surface hosts (local ADR 0055 §3): the Tasks
/// view's filtered rows on a month or a week by due date, under the same filter bar and
/// beside the same detail panel, on the window's own task state. The Calendar hands it the
/// range, the Plans layer and the hours worked a day; the Events layer it still reads
/// itself until that layer moves.
/// </summary>
public sealed class TasksCalendarPart : TasksSurface
{
    /// <summary>What the Calendar hands the part; the name is
    /// <see cref="CalendarTasksLayer.LayerParameter"/>.</summary>
    [Parameter]
    public CalendarLayerInputs? Layer { get; set; }

    protected override TasksLayout Layout => TasksLayout.Calendar;

    protected override CalendarLayerInputs? CalendarLayer => Layer;
}
