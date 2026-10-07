namespace Backlog.UI.Components.Day;

/// <summary>
/// One step of a task as <see cref="DayTaskDetail"/> lists it: the sub-item's id,
/// its title, and whether it is ticked.
/// </summary>
/// <param name="Id">The sub-item's id, handed back when the step is ticked.</param>
/// <param name="Title">The step's title, as the task's text writes it.</param>
/// <param name="Done">Ticked off.</param>
public sealed record DayStep(Guid Id, string Title, bool Done);
