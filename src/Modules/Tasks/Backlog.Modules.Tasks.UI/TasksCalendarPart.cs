using Backlog.UI.Components.Calendar;
using Microsoft.AspNetCore.Components;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The Tasks layer as a part the Calendar surface hosts (local ADR 0055 §3): the Tasks
/// view's filtered rows on a month or a week by due date, under the same filter bar and
/// beside the same detail panel, on the window's own task state. The Calendar hands it the
/// range, the Plans and Events layers and the hours worked a day.
/// <para>
/// The Tasks pane keeps no calendar of its own: the part hands it
/// <see cref="TasksCalendarBody"/> as the body drawn under the filter bar.
/// </para>
/// </summary>
public sealed class TasksCalendarPart : TasksSurface
{
    /// <summary>What the Calendar hands the part; the name is
    /// <see cref="CalendarTasksLayer.LayerParameter"/>.</summary>
    [Parameter]
    public CalendarLayerInputs? Layer { get; set; }

    protected override RenderFragment<TasksPaneBody>? Body => pane => builder =>
    {
        builder.OpenComponent<TasksCalendarBody>(0);
        builder.AddComponentParameter(1, nameof(TasksCalendarBody.Pane), pane);
        builder.AddComponentParameter(2, nameof(TasksCalendarBody.Layer), Layer);
        builder.CloseComponent();
    };
}
