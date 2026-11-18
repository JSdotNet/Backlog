namespace Backlog.UI.Components.Calendar;

/// <summary>
/// The Tasks layer's part (local ADR 0055 §3): the component Tasks offers for the Calendar
/// surface to host — the filter bar, the tasks by due date, the tray, the hours, the drag
/// and the open task, on the window's own task state.
/// <para>
/// Tasks registers one; the Calendar renders it through <c>DynamicComponent</c> and hands
/// it one parameter, named by <see cref="LayerParameter"/>, of type
/// <see cref="CalendarLayerInputs"/>. A contract between two modules that may not see each
/// other, so it lives here, on the precedent of <c>GoalControlSlot</c>.
/// </para>
/// </summary>
/// <param name="Component">The part's component type.</param>
public sealed record CalendarTasksLayer(Type Component)
{
    /// <summary>The name of the part's one parameter, a <see cref="CalendarLayerInputs"/>.</summary>
    public const string LayerParameter = "Layer";
}
